using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Operations;

public class SalaryOperationsService(AppDbContext db, TelegramService telegramService)
{
    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;

    public async Task<int> GetCurrentSalaryAsync(Guid employeeId, CancellationToken ct)
    {
        return await _db.SalaryOperations
            .Where(s => s.EmployeeId == employeeId)
            .AsNoTracking()
            .SumAsync(
                o => o.Type == SalaryOperationType.Regular || o.Type == SalaryOperationType.Bonus
                    ? o.Amount
                    : -o.Amount,
                ct);
    }

    public async Task<int> ApplySalaryOperationAsync(
        Guid employeeId,
        int signedAmount,
        SalaryOperationType type,
        string comment,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (signedAmount == 0)
        {
            return await GetCurrentSalaryAsync(employeeId, ct);
        }

        var currentSalary = await GetCurrentSalaryAsync(employeeId, ct);
        var updatedSalary = currentSalary + signedAmount;

        _db.SalaryOperations.Add(new SalaryOperationEntity
        {
            EmployeeId = employeeId,
            Amount = Math.Abs(signedAmount),
            Type = type,
            Comment = comment
        });

        await _telegramService.SendSalaryAsync(
            employeeId,
            signedAmount,
            type,
            updatedSalary,
            comment,
            scope,
            ct);

        return updatedSalary;
    }
}
