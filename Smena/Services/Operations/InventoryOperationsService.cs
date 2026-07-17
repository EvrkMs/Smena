using Host.Services.Data;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Operations;

public class InventoryOperationsService(
    AppDbContext db,
    SalaryOperationsService salaryOperationsService)
{
    private readonly AppDbContext _db = db;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;

    public async Task<OperationResult> SendInventoryAsync(
        int totalAmount,
        IReadOnlyList<Guid> employeeIds,
        string? comment,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (totalAmount <= 0)
        {
            return OperationResult.Fail("Invalid amount.");
        }

        if (employeeIds.Count == 0)
        {
            return OperationResult.Fail("No employees selected.");
        }

        var ids = employeeIds.Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => ids.Contains(e.Id))
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        // Требуем ВСЕХ запрошенных: раньше проверялся только Count == 0, и при
        // удалённом/неизвестном сотруднике вся сумма молча делилась между
        // найденными (9000 на троих превращалось в 4500/4500 на двоих).
        if (employees.Count != ids.Count)
        {
            return OperationResult.Fail(
                "Часть сотрудников не найдена (возможно, кто-то удалён). Обновите список и повторите.");
        }

        int perEmployee = totalAmount / employees.Count;
        int remainder = totalAmount % employees.Count;
        var resolvedComment = string.IsNullOrWhiteSpace(comment)
            ? "Инвентаризация"
            : comment;

        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            // Балансы ЗП меняются только под блокировками (порядок — по Id).
            await AdvisoryLocks.AcquireEmployeeSalariesAsync(_db, ids, ct);

            foreach (var employee in employees)
            {
                int amount = perEmployee + (remainder > 0 ? 1 : 0);
                if (remainder > 0)
                {
                    remainder--;
                }

                await _salaryOperationsService.ApplySalaryOperationAsync(
                    employee.Id,
                    -amount,
                    Data.Entities.SalaryOperationType.Inventory,
                    resolvedComment,
                    scope,
                    ct);
            }

            await _db.SaveChangesAsync(ct);
        }, ct);

        return OperationResult.Ok("Inventory processed.");
    }
}
