using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Host.Services.Telegram;

public sealed class TelegramMessageScope(ITelegramBotClient botClient, ILogger<TelegramMessageScope> logger)
{
    private readonly ITelegramBotClient _botClient = botClient;
    private readonly ILogger<TelegramMessageScope> _logger = logger;
    private readonly List<(long ChatId, int MessageId)> _messages = [];

    public void Track(long chatId, int messageId)
    {
        _messages.Add((chatId, messageId));
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        foreach (var (chatId, messageId) in _messages)
        {
            try
            {
                await _botClient.DeleteMessage(chatId, messageId, ct);
            }
            catch
            {
                // best-effort rollback; log and continue
                _logger.LogWarning("Failed to delete Telegram message {MessageId} in chat {ChatId} during rollback.", messageId, chatId);
            }
        }
    }
}
