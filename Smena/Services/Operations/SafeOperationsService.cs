using System.Threading.Channels;
using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Operations;

public class SafeOperationsService(
    AppDbContext db,
    TelegramService telegramService,
    SafeUpdatesNotifier notifier)
{
    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;
    private readonly SafeUpdatesNotifier _notifier = notifier;

    public async Task<int> GetCurrentSafeAsync(CancellationToken ct)
    {
        return await _db.SafeOperations
            .AsNoTracking()
            .SumAsync(
                o => o.Type == SafeOperationType.Coming ? o.Amount : -o.Amount,
                ct);
    }

    public async Task<int> ApplySafeOperationAsync(
        int signedAmount,
        string comment,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var currentSafe = await GetCurrentSafeAsync(ct);
        return await ApplySafeOperationAsync(signedAmount, comment, currentSafe, scope, ct);
    }

    public async Task<int> ApplySafeOperationAsync(
        int signedAmount,
        string comment,
        int currentSafe,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (signedAmount == 0)
        {
            return currentSafe;
        }

        var updatedSafe = currentSafe + signedAmount;

        _db.SafeOperations.Add(new SafeOperationEntity
        {
            Amount = Math.Abs(signedAmount),
            Comment = comment,
            Type = signedAmount > 0 ? SafeOperationType.Coming : SafeOperationType.Expense
        });

        await _telegramService.SendSafeAsync(
            signedAmount,
            comment,
            updatedSafe,
            scope,
            ct);

        // ВАЖНО: publish в SubscribeSafe-стримы здесь НЕ делается. Этот метод
        // выполняется внутри транзакции вызывающего; публикация до коммита
        // рассылала клиентам баланс, который при откате (например, протухшая
        // фото-сессия в расходе) никогда не попадал в БД — тикер показывал
        // фантомные деньги. Telegram-сообщение выше компенсируется интерцептором
        // при откате, у нотификатора компенсации нет — поэтому вызывающий обязан
        // опубликовать итог сам ПОСЛЕ коммита через PublishSafeUpdate.
        return updatedSafe;
    }

    /// <summary>Публикация нового баланса подписчикам — только ПОСЛЕ коммита.</summary>
    public void PublishSafeUpdate(int updatedSafe) => _notifier.Publish(updatedSafe);

    public Channel<long> Subscribe() => _notifier.Subscribe();

    public void Unsubscribe(Channel<long> channel) => _notifier.Unsubscribe(channel);

    /// <summary>
    /// Applies a safe operation within a transaction and persists changes.
    /// Use this when the safe operation is the only DB change in the call.
    /// </summary>
    public async Task ApplyAndSaveAsync(
        int signedAmount,
        string comment,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var updatedSafe = await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            // Баланс читается под advisory-блокировкой внутри транзакции —
            // иначе два конкурентных вызова считают его от одного значения.
            await AdvisoryLocks.AcquireSafeAsync(_db, ct);
            var updated = await ApplySafeOperationAsync(signedAmount, comment, scope, ct);
            await _db.SaveChangesAsync(ct);
            return updated;
        }, ct);

        PublishSafeUpdate(updatedSafe);
    }
}
