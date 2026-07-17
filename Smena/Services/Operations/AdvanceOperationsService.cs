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

        var type = isSalary ? SalaryOperationType.Pay : SalaryOperationType.Advance;
        var resolvedComment = string.IsNullOrWhiteSpace(comment)
            ? (isSalary ? "ЗП" : "Аванс")
            : comment;

        bool fromSafe = extractFromSafe && !isNonCash;
        int? updatedSafe = null;

        var result = await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            // Общий порядок блокировок приложения: сейф → сотрудник.
            if (fromSafe)
            {
                await AdvisoryLocks.AcquireSafeAsync(_db, ct);
            }
            await AdvisoryLocks.AcquireEmployeeSalaryAsync(_db, employeeId, ct);

            // Проверка лимита — ВНУТРИ транзакции и под блокировкой: раньше два
            // конкурентных вызова (ретрай сети, второй терминал) читали ЗП до
            // транзакции, оба проходили проверку и выплачивали сумму дважды.
            var currentSalary = await _salaryOperationsService.GetCurrentSalaryAsync(employeeId, ct);

            if (currentSalary <= 0)
            {
                return OperationResult.Fail("У сотрудника нет доступной ЗП для выплаты.");
            }

            if (amount > currentSalary)
            {
                return OperationResult.Fail($"Нельзя выдать больше текущей ЗП ({currentSalary} руб.).");
            }

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
                updatedSafe = await _safeOperationsService.ApplySafeOperationAsync(
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
            return OperationResult.Ok("Operation completed.");
        }, ct);

        if (result.Success && updatedSafe.HasValue)
        {
            _safeOperationsService.PublishSafeUpdate(updatedSafe.Value);
        }

        return result;
    }

    private static int SignedSalaryAmount(SalaryOperationType type, int amount)
        => type switch
        {
            SalaryOperationType.Regular or SalaryOperationType.Bonus => amount,
            _ => -amount
        };
}
