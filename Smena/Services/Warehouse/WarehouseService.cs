using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Host.Services.Warehouse;

public sealed class WarehouseStockItem
{
    public string Name { get; init; } = string.Empty;
    public string? Article { get; init; }
    public string? Code { get; init; }
    public string? Folder { get; init; }
    public double Stock { get; init; }
    public double Reserve { get; init; }
    public double InTransit { get; init; }
    public double Quantity { get; init; }
    /// <summary>Price in rubles (already divided by 100).</summary>
    public decimal Price { get; init; }
}

public sealed class WarehouseService(
    SkladHttpClient skladClient,
    IOptions<WarehouseOptions> options,
    ILogger<WarehouseService> logger)
{
    private readonly WarehouseOptions _options = options.Value;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => _options.Enabled;

    public async Task<(List<WarehouseStockItem> Items, string? Error)> GetStockAsync(
        string? search,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return ([], null);

        try
        {
            var url = BuildStockUrl(search);
            var response = await skladClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("МойСклад returned {Status}: {Body}", response.StatusCode, body);
                return ([], $"МойСклад вернул ошибку {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<MoySkladStockResponse>(json, _json, ct);

            var items = (result?.Rows ?? [])
                .Select(r => new WarehouseStockItem
                {
                    Name = r.Name,
                    Article = r.Article,
                    Code = r.Code,
                    Folder = r.Folder?.PathName ?? r.Folder?.Name,
                    Stock = r.Stock,
                    Reserve = r.Reserve,
                    InTransit = r.InTransit,
                    Quantity = r.Quantity,
                    Price = (decimal)(r.Price / 100.0)
                })
                .OrderBy(x => x.Folder)
                .ThenBy(x => x.Name)
                .ToList();

            return (items, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("МойСклад request timed out after 10s");
            return ([], "МойСклад не ответил за 10 секунд — проверьте соединение или токен");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch МойСклад stock");
            return ([], $"Ошибка при обращении к МойСклад: {ex.Message}");
        }
    }

    private string BuildStockUrl(string? search)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/report/stock/all?limit={_options.Limit}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search.Trim())}";
        return url;
    }
}
