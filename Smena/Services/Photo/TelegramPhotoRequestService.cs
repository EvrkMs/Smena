using Host.Services.Data;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Host.Services.Photo;

public class TelegramPhotoRequestService(
    TelegramService telegramService,
    TelegramUpdatesPoller updatesPoller,
    AppDbContext db,
    PhotoSessionStore sessionStore,
    IOptions<PhotoOptions> options,
    ILogger<TelegramPhotoRequestService> logger)
{
    private static readonly TimeSpan AlbumWindow = TimeSpan.FromSeconds(2);
    private const int MaxPhotos = 10;

    private readonly TelegramService _telegramService = telegramService;
    private readonly TelegramUpdatesPoller _updatesPoller = updatesPoller;
    private readonly AppDbContext _db = db;
    private readonly PhotoSessionStore _sessionStore = sessionStore;
    private readonly PhotoOptions _options = options.Value;
    private readonly ILogger<TelegramPhotoRequestService> _logger = logger;

    public async Task<(bool Success, string Message, string? SessionKey)> RequestPhotosAsync(
        Guid employeeId,
        Func<PhotoProgress, Task>? progress,
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

        var telegramId = employee.TelegramAccount.TelegramId;

        if (progress != null)
        {
            await progress(new PhotoProgress(PhotoProgressStage.Start));
        }

        // Подписка — ДО отправки запроса сотруднику, чтобы фото, отправленное
        // мгновенно, не проскочило мимо. Свой getUpdates-цикл здесь больше не
        // крутится: апдейты раздаёт общий TelegramUpdatesPoller.
        using var subscription = _updatesPoller.Subscribe(update =>
            update.Message is { Photo: not null } message && message.Chat.Id == telegramId);

        var requestTime = DateTime.UtcNow;

        try
        {
            await _telegramService.ExecuteWithRetryAsync(
                (bot, token) => bot.SendMessage(
                    telegramId,
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
                telegramId);
            return (false, $"Не удалось отправить сообщение в Telegram: {ex.Message}", null);
        }

        if (progress != null)
        {
            await progress(new PhotoProgress(PhotoProgressStage.RequestSent));
        }

        var deadline = requestTime + timeout;
        var fileIds = new List<string>();
        string? mediaGroupId = null;

        while (true)
        {
            // До первого фото ждём вплоть до дедлайна; после — окно альбома 2 с
            // (каждое новое фото продлевает его), но не дольше дедлайна.
            var remaining = deadline - DateTime.UtcNow;
            if (fileIds.Count > 0 && AlbumWindow < remaining)
            {
                remaining = AlbumWindow;
            }

            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            Update update;
            try
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(remaining);
                update = await subscription.Reader.ReadAsync(readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Общий таймаут либо закрылось окно альбома.
                break;
            }

            var message = update.Message!;
            if (message.Date.ToUniversalTime() <= requestTime)
            {
                continue;
            }

            mediaGroupId ??= message.MediaGroupId ?? "single";
            if (message.MediaGroupId != null && message.MediaGroupId != mediaGroupId)
            {
                continue;
            }

            if (fileIds.Count >= MaxPhotos)
            {
                continue;
            }

            var fileId = message.Photo!.Last().FileId;
            if (!fileIds.Contains(fileId))
            {
                fileIds.Add(fileId);
                if (progress != null)
                {
                    await progress(new PhotoProgress(PhotoProgressStage.PhotosReceived, fileIds.Count));
                }
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
