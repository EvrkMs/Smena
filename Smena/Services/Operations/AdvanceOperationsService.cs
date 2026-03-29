using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Operations;

public class AdvanceOperationsService(
    AppDbContext db,
    SalaryOperationsService salaryOperationsService,
    SafeOperationsService safeOperationsService,
    NonCashOperationsService nonCashOperationsService)
{
    private readonly AppDbContext _db = db;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly NonCashOperationsService _nonCashOperationsService = nonCashOperationsService;

    public async Task<OperationResult> SendAdvanceAsync(
        Guid employeeId,
        int amount,
        bool isSalary,
        string? comment,
        bool extractFromSafe,
        bool isNonCash,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (amount <= 0)
        {
            return OperationResult.Fail("Invalid amount.");
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);

        if (employee == null)
        {
            return OperationResult.Fail("Employee not found.");
        }

        var currentSalary = await _salaryOperationsService.GetCurrentSalaryAsync(employeeId, ct);

        if (currentSalary <= 0)
        {
            return OperationResult.Fail("У сотрудника нет доступной ЗП для выплаты.");
        }

        if (amount > currentSalary)
        {
            return OperationResult.Fail($"Нельзя выдать больше текущей ЗП ({currentSalary} руб.).");
        }

        var type = isSalary ? SalaryOperationType.Pay : SalaryOperationType.Advance;
        var resolvedComment = string.IsNullOrWhiteSpace(comment)
            ? (isSalary ? "ЗП" : "Аванс")
            : comment;

        bool fromSafe = extractFromSafe && !isNonCash;

        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            var signedDelta = SignedSalaryAmount(type, amount);
            await _salaryOperationsService.ApplySalaryOperationAsync(
                employeeId,
                signedDelta,
                type,
                resolvedComment,
                scope,
                ct);

            if (fromSafe)
            {
                await _safeOperationsService.ApplySafeOperationAsync(
                    -amount,
                    $"{employee.Name}: {resolvedComment}",
                    scope,
                    ct);
            }
            else if (isNonCash)
            {
                _nonCashOperationsService.AddExpense(
                    amount,
                    $"{employee.Name}: {resolvedComment}");
            }

            await _db.SaveChangesAsync(ct);
        }, ct);

        return OperationResult.Ok("Operation completed.");
    }

    private static int SignedSalaryAmount(SalaryOperationType type, int amount)
        => type switch
        {
            SalaryOperationType.Regular or SalaryOperationType.Bonus => amount,
            _ => -amount
        };
}
