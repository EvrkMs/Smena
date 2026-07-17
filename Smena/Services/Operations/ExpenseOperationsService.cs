using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Photo;
using Host.Services.Telegram;
using Microsoft.Extensions.Options;

namespace Host.Services.Operations;

public class ExpenseOperationsService(
    AppDbContext db,
    TelegramService telegramService,
    PhotoSessionStore photoSessionStore,
    IOptions<PhotoOptions> photoOptions,
    SafeOperationsService safeOperationsService,
    NonCashOperationsService nonCashOperationsService)
{
    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;
    private readonly PhotoSessionStore _photoSessionStore = photoSessionStore;
    private readonly PhotoOptions _photoOptions = photoOptions.Value;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly NonCashOperationsService _nonCashOperationsService = nonCashOperationsService;

    public async Task<OperationResult> AddExpenseAsync(
        int amount,
        string? comment,
        bool fromSafe,
        bool isNonCash,
        bool sendPhoto,
        string? photoSessionKey,
        string? senderName,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (amount <= 0)
        {
            return OperationResult.Fail("Invalid amount.");
        }

        bool extractFromSafe = fromSafe && !isNonCash;
        int? updatedSafe = null;

        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            var expense = new ExpenseEntity
            {
                Amount = amount,
                Comment = comment ?? string.Empty,
                FromSafe = extractFromSafe,
                IsNonCash = isNonCash,
                SendPhoto = sendPhoto,
                PhotoSessionKey = string.IsNullOrWhiteSpace(photoSessionKey) ? null : photoSessionKey,
                SenderName = senderName ?? string.Empty
            };

            _db.Expenses.Add(expense);

            if (extractFromSafe)
            {
                // Баланс сейфа меняется только под advisory-блокировкой (см. AdvisoryLocks).
                await AdvisoryLocks.AcquireSafeAsync(_db, ct);
                updatedSafe = await _safeOperationsService.ApplySafeOperationAsync(
                    -amount,
                    $"Расход: {comment}",
                    scope,
                    ct);
            }
            else if (isNonCash)
            {
                var nonCashComment = string.IsNullOrWhiteSpace(comment)
                    ? "Расход (Б/Н)"
                    : $"Расход (Б/Н): {comment}";

                _nonCashOperationsService.AddExpense(amount, nonCashComment);
            }

            if (sendPhoto)
            {
                if (string.IsNullOrWhiteSpace(photoSessionKey))
                {
                    throw new InvalidOperationException("Photo session key is required.");
                }

                if (!_photoSessionStore.TryGetSession(
                        photoSessionKey,
                        TimeSpan.FromSeconds(_photoOptions.SessionTtlSeconds),
                        out var fileIds))
                {
                    throw new InvalidOperationException("Photos not found or session expired.");
                }

                await _telegramService.SendExpensePhotosAsync(fileIds, scope, ct);
            }

            await _telegramService.SendExpenseAsync(expense, scope, ct);

            await _db.SaveChangesAsync(ct);
        }, ct);

        if (!string.IsNullOrWhiteSpace(photoSessionKey))
        {
            _photoSessionStore.RemoveSession(photoSessionKey);
        }

        // Публикация подписчикам — строго после коммита: publish внутри транзакции
        // при откате показывал клиентам списание, которого не было в БД.
        if (updatedSafe.HasValue)
        {
            _safeOperationsService.PublishSafeUpdate(updatedSafe.Value);
        }

        return OperationResult.Ok("Расход добавлен.");
    }
}
