using Host.Services.Data;
using Host.Services.Photo;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Host.Services.Photo;

public class TelegramPhotoRequestService(
    TelegramService telegramService,
    AppDbContext db,
    PhotoSessionStore sessionStore,
    TelegramUpdateOffsetStore offsetStore,
    IOptions<PhotoOptions> options,
    ILogger<TelegramPhotoRequestService> logger)
{
    private readonly TelegramService _telegramService = telegramService;
    private readonly AppDbContext _db = db;
    private readonly PhotoSessionStore _sessionStore = sessionStore;
    private readonly TelegramUpdateOffsetStore _offsetStore = offsetStore;
    private readonly PhotoOptions _options = options.Value;
    private readonly ILogger<TelegramPhotoRequestService> _logger = logger;

    public async Task<(bool Success, string Message, string? SessionKey)> RequestPhotosAsync(
        Guid employeeId,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var operationCt = linkedCts.Token;

        var employee = await _db.Employees
            .Include(e => e.TelegramAccount)
            .FirstOrDefaultAsync(e => e.Id == employeeId, operationCt);

        if (employee?.TelegramAccount == null)
        {
            return (false, "Telegram account not found.", null);
        }

        if (progress != null)
        {
            await progress("Start");
        }

        try
        {
            await _telegramService.ExecuteWithRetryAsync(
                (bot, token) => bot.SendMessage(
                    employee.TelegramAccount.TelegramId,
                    "Ожидаю фото...",
                    cancellationToken: token),
                operationCt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "Не удалось отправить сообщение в Telegram: timeout.", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Failed to send photo request message to Telegram for employeeId={EmployeeId} telegramId={TelegramId}",
                employee.Id,
                employee.TelegramAccount.TelegramId);
            return (false, $"Не удалось отправить сообщение в Telegram: {ex.Message}", null);
        }

        if (progress != null)
        {
            await progress("Request sent");
        }

        var requestTime = DateTime.UtcNow;
        var albumWindow = TimeSpan.FromSeconds(2);
        var fileIds = new List<string>();
        string? mediaGroupId = null;
        DateTime? lastPhotoAt = null;

        while (DateTime.UtcNow - requestTime < timeout)
        {
            operationCt.ThrowIfCancellationRequested();

            var updates = await _telegramService.ExecuteWithRetryAsync(
                (bot, token) => bot.GetUpdates(
                    offset: _offsetStore.GetOffset(),
                    timeout: 1,
                    cancellationToken: token),
                operationCt);

            if (updates.Length > 0)
            {
                _offsetStore.AdvanceOffset(updates.Max(u => u.Id) + 1);
            }

            foreach (var update in updates)
            {
                if (update.Type != UpdateType.Message)
                {
                    continue;
                }

                var message = update.Message;
                if (message?.Photo == null || message.Chat.Id != employee.TelegramAccount.TelegramId)
                {
                    continue;
                }

                if (message.Date.ToUniversalTime() <= requestTime)
                {
                    continue;
                }

                mediaGroupId ??= message.MediaGroupId ?? "single";
                if (message.MediaGroupId != null && message.MediaGroupId != mediaGroupId)
                {
                    continue;
                }

                if (fileIds.Count >= 10)
                {
                    continue;
                }

                var fileId = message.Photo.Last().FileId;
                if (!fileIds.Contains(fileId))
                {
                    fileIds.Add(fileId);
                    if (progress != null)
                    {
                        await progress($"Received {fileIds.Count} photos");
                    }
                }

                lastPhotoAt = DateTime.UtcNow;
            }

            if (fileIds.Count > 0 && lastPhotoAt.HasValue &&
                DateTime.UtcNow - lastPhotoAt.Value > albumWindow)
            {
                break;
            }
        }

        if (fileIds.Count == 0)
        {
            return (false, "Photo wait timeout.", null);
        }

        var sessionKey = _sessionStore.CreateSession(fileIds);
        return (true, "Photos received.", sessionKey);
    }
}
