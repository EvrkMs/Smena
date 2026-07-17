using Microsoft.Extensions.Options;
using System.Net.Http;
using Telegram.Bot;

namespace Host.Services.Telegram;

/// <summary>
/// Единственный владелец общего ITelegramBotClient (раньше — static-поле внутри
/// scoped TelegramService). Вынесен в синглтон, потому что клиент нужен и
/// scoped-сервисам, и фоновому TelegramUpdatesPoller.
/// </summary>
public sealed class TelegramBotClientFactory(
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotClientFactory> logger)
{
    private readonly TelegramOptions _options = options.Value;
    private readonly ILogger<TelegramBotClientFactory> _logger = logger;
    private readonly object _lock = new();
    private ITelegramBotClient? _client;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Token);

    public ITelegramBotClient GetClientOrThrow()
    {
        if (_client != null)
        {
            return _client;
        }

        lock (_lock)
        {
            if (_client != null)
            {
                return _client;
            }

            if (!IsConfigured)
            {
                throw new InvalidOperationException("Telegram token is not configured.");
            }

            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15)
            };

            if (!string.IsNullOrWhiteSpace(_options.ProxyUri))
            {
                handler.Proxy = new System.Net.WebProxy(_options.ProxyUri);
                handler.UseProxy = true;
                _logger.LogInformation("Telegram: using proxy {ProxyUri}", _options.ProxyUri);
            }

            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds)
            };

            _client = new TelegramBotClient(_options.Token, httpClient);
            return _client;
        }
    }
}
