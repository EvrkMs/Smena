using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Operations;

public class SafeOperationsService(AppDbContext db, TelegramService telegramService)
{
    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;

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

        return updatedSafe;
    }
}
