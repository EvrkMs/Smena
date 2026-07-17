using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Host.Services.Telegram;

/// <summary>
/// Единственный потребитель getUpdates у бота.
///
/// Раньше каждый фото-запрос поллил Telegram сам (timeout:1 — до ~180 HTTPS-
/// запросов на одно ожидание) и двигал общий in-memory offset: параллельные
/// запросы крали апдейты друг у друга (а Telegram на конкурентный getUpdates
/// отвечает 409 Conflict), любой не-фото апдейт уничтожался навсегда. Теперь
/// поллинг централизован: long-poll 25 с, каждый апдейт раздаётся ВСЕМ активным
/// подпискам, чей фильтр совпал. Новые сценарии («ответ от определённого id»
/// и т.п.) подписываются через Subscribe с собственным фильтром.
///
/// Offset не персистится сознательно: при старте инициализируемся последним
/// апдейтом в очереди и берём только новые — бэклог за время простоя
/// пропускается (раньше он перечитывался целиком и фильтровался по дате).
/// </summary>
public sealed class TelegramUpdatesPoller(
    TelegramBotClientFactory clientFactory,
    ILogger<TelegramUpdatesPoller> logger) : BackgroundService
{
    private const int LongPollSeconds = 25;
    private const int ErrorRetryDelayMs = 3000;

    private readonly TelegramBotClientFactory _clientFactory = clientFactory;
    private readonly ILogger<TelegramUpdatesPoller> _logger = logger;
    private readonly object _lock = new();
    private readonly List<Subscription> _subscriptions = [];

    public sealed class Subscription : IDisposable
    {
        private readonly TelegramUpdatesPoller _owner;

        internal Subscription(TelegramUpdatesPoller owner, Func<Update, bool> filter)
        {
            _owner = owner;
            Filter = filter;
            Channel = System.Threading.Channels.Channel.CreateUnbounded<Update>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        }

        internal Func<Update, bool> Filter { get; }
        internal Channel<Update> Channel { get; }

        public ChannelReader<Update> Reader => Channel.Reader;

        public void Dispose() => _owner.Unsubscribe(this);
    }

    /// <summary>Подписка на апдейты по фильтру. Обязательно Dispose по завершении сценария.</summary>
    public Subscription Subscribe(Func<Update, bool> filter)
    {
        var subscription = new Subscription(this, filter);
        lock (_lock)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_lock)
        {
            _subscriptions.Remove(subscription);
        }

        subscription.Channel.Writer.TryComplete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_clientFactory.IsConfigured)
        {
            _logger.LogWarning("Telegram token is not configured — updates poller is disabled.");
            return;
        }

        var client = _clientFactory.GetClientOrThrow();
        int? offset = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (offset is null)
                {
                    // offset = -1 отдаёт только последний апдейт — от него и продолжаем,
                    // пропуская накопившийся за простой бэклог.
                    var last = await client.GetUpdates(
                        offset: -1,
                        limit: 1,
                        timeout: 0,
                        allowedUpdates: [UpdateType.Message],
                        cancellationToken: stoppingToken);

                    offset = last.Length > 0 ? last[^1].Id + 1 : 0;
                    continue;
                }

                var updates = await client.GetUpdates(
                    offset: offset.Value,
                    timeout: LongPollSeconds,
                    allowedUpdates: [UpdateType.Message],
                    cancellationToken: stoppingToken);

                if (updates.Length == 0)
                {
                    continue;
                }

                offset = updates.Max(u => u.Id) + 1;
                Dispatch(updates);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram getUpdates failed — retrying in {DelayMs} ms.", ErrorRetryDelayMs);
                try
                {
                    await Task.Delay(ErrorRetryDelayMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void Dispatch(IReadOnlyList<Update> updates)
    {
        Subscription[] snapshot;
        lock (_lock)
        {
            snapshot = [.. _subscriptions];
        }

        foreach (var update in updates)
        {
            foreach (var subscription in snapshot)
            {
                try
                {
                    if (subscription.Filter(update))
                    {
                        subscription.Channel.Writer.TryWrite(update);
                    }
                }
                catch
                {
                    // Фильтр подписчика не должен ронять общий поллер.
                }
            }
        }
    }
}
