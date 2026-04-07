using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Host.Services.Warehouse;

/// <summary>
/// Позиция из кэша МойСклад.
/// </summary>
public sealed class SkladCacheItem
{
    public string Name    { get; init; } = string.Empty;
    public string? Article { get; init; }
    public string? Code    { get; init; }
    public string? Folder  { get; init; }
    /// <summary>Физический остаток на момент последнего обновления кэша.</summary>
    public double Stock { get; init; }
}

/// <summary>
/// Фоновый сервис — загружает ВСЕ позиции из МойСклад один раз при старте
/// и затем периодически обновляет кэш. Даёт мгновенный поиск по названию/артикулу.
/// </summary>
public sealed class SkladCacheService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<WarehouseOptions> _options;
    private readonly ILogger<SkladCacheService> _logger;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // volatile: фоновый поток пишет, основные потоки читают — race-free обмен ссылкой
    private volatile IReadOnlyList<SkladCacheItem> _items = [];
    private DateTime _lastRefreshed = DateTime.MinValue;

    public IReadOnlyList<SkladCacheItem> Items       => _items;
    public DateTime                      LastRefreshed => _lastRefreshed;
    public bool                          IsReady       => _items.Count > 0;

    public SkladCacheService(
        IServiceScopeFactory scopeFactory,
        IOptions<WarehouseOptions> options,
        ILogger<SkladCacheService> logger)
    {
        _scopeFactory = scopeFactory;
        _options      = options;
        _logger       = logger;
    }

    // ─── Public API ────────────────────────────────────────────────

    /// <summary>Быстрый поиск по кэшу (до <paramref name="limit"/> результатов).</summary>
    public IReadOnlyList<SkladCacheItem> Search(string? query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = query.Trim();
        return _items
            .Where(x =>
                x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (x.Article != null && x.Article.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (x.Code    != null && x.Code.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Take(limit)
            .ToList();
    }

    /// <summary>Вручную запустить обновление (например, по кнопке на UI).</summary>
    public Task ForceRefreshAsync(CancellationToken ct = default) => RefreshAsync(ct);

    // ─── BackgroundService ─────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Первая загрузка — сразу при старте
        await RefreshAsync(stoppingToken);

        var minutes = _options.Value.CacheRefreshMinutes > 0
            ? _options.Value.CacheRefreshMinutes
            : 30;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshAsync(stoppingToken);
    }

    // ─── Internals ─────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (!_options.Value.Enabled) return;

        try
        {
            _logger.LogInformation("[SkladCache] Refreshing items cache...");

            using var scope = _scopeFactory.CreateScope();
            var client      = scope.ServiceProvider.GetRequiredService<SkladHttpClient>();
            var baseUrl     = _options.Value.BaseUrl.TrimEnd('/');
            const int pageSize = 1000;

            // ── 1. Остатки из отчёта (есть только у товаров с историей движений) ──
            var stockMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            {
                var offset = 0;
                while (true)
                {
                    var url  = $"{baseUrl}/report/stock/all?limit={pageSize}&offset={offset}&stockMode=all";
                    var resp = await client.GetAsync(url, ct);
                    if (!resp.IsSuccessStatusCode) { _logger.LogWarning("[SkladCache] stock/all returned {Status}", resp.StatusCode); break; }

                    var data = await JsonSerializer.DeserializeAsync<MoySkladStockResponse>(
                        await resp.Content.ReadAsStreamAsync(ct), _json, ct);

                    if (data?.Rows is null || data.Rows.Count == 0) break;
                    foreach (var r in data.Rows)
                        stockMap[StockKey(r.Name, r.Article)] = r.Stock;

                    if (data.Rows.Count < pageSize) break;
                    offset += pageSize;
                }
            }

            // ── 2. Полный каталог из ассортимента (включая товары без движений) ──
            var all    = new List<SkladCacheItem>();
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            {
                var offset = 0;
                while (true)
                {
                    // type=product,variant — только физические товары, без услуг и комплектов
                    var url  = $"{baseUrl}/entity/assortment?limit={pageSize}&offset={offset}";
                    var resp = await client.GetAsync(url, ct);
                    if (!resp.IsSuccessStatusCode) { _logger.LogWarning("[SkladCache] assortment returned {Status}", resp.StatusCode); break; }

                    var data = await JsonSerializer.DeserializeAsync<MoySkladAssortmentResponse>(
                        await resp.Content.ReadAsStreamAsync(ct), _json, ct);

                    if (data?.Rows is null || data.Rows.Count == 0) break;

                    foreach (var r in data.Rows)
                    {
                        var type = r.Meta?.Type;
                        if (type is not ("product" or "variant")) continue;

                        // Для variant отображаем имя родителя + название модификации
                        var name    = r.Name;
                        var folder  = r.ProductFolder?.PathName ?? r.ProductFolder?.Name
                                   ?? r.Product?.ProductFolder?.PathName ?? r.Product?.ProductFolder?.Name;
                        var key     = StockKey(name, r.Article);

                        if (!seen.Add(key)) continue; // дедупликация

                        stockMap.TryGetValue(key, out var stock);
                        all.Add(new SkladCacheItem
                        {
                            Name    = name,
                            Article = r.Article,
                            Code    = r.Code,
                            Folder  = folder,
                            Stock   = stock,
                        });
                    }

                    if (data.Rows.Count < pageSize) break;
                    offset += pageSize;
                }
            }

            // Если ассортимент не вернул ничего (нет прав / ошибка) — фолбэк на stock-данные
            if (all.Count == 0 && stockMap.Count > 0)
            {
                _logger.LogWarning("[SkladCache] Assortment empty, falling back to stock/all data");
                // (повторный запрос не нужен — stockMap уже заполнен, но нет папок/артикулов)
                // Пересоздаём из отчёта
                using var scope2 = _scopeFactory.CreateScope();
                var client2      = scope2.ServiceProvider.GetRequiredService<SkladHttpClient>();
                var offset2      = 0;
                while (true)
                {
                    var url  = $"{baseUrl}/report/stock/all?limit={pageSize}&offset={offset2}&stockMode=all";
                    var resp = await client2.GetAsync(url, ct);
                    if (!resp.IsSuccessStatusCode) break;
                    var data = await JsonSerializer.DeserializeAsync<MoySkladStockResponse>(
                        await resp.Content.ReadAsStreamAsync(ct), _json, ct);
                    if (data?.Rows is null || data.Rows.Count == 0) break;
                    foreach (var r in data.Rows)
                        all.Add(new SkladCacheItem { Name = r.Name, Article = r.Article, Code = r.Code,
                            Folder = r.Folder?.PathName ?? r.Folder?.Name, Stock = r.Stock });
                    if (data.Rows.Count < pageSize) break;
                    offset2 += pageSize;
                }
            }

            all.Sort((a, b) =>
            {
                var f = string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
                return f != 0 ? f : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            _items         = all;
            _lastRefreshed = DateTime.UtcNow;
            _logger.LogInformation("[SkladCache] Ready: {Count} items ({Stock} with stock data)",
                all.Count, stockMap.Count);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SkladCache] Refresh failed — cache remains stale");
        }
    }

    private static string StockKey(string name, string? article)
        => string.IsNullOrEmpty(article) ? name : $"{name}|{article}";
}
