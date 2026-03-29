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

        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        if (employees.Count == 0)
        {
            return OperationResult.Fail("Employees not found.");
        }

        int perEmployee = totalAmount / employees.Count;
        int remainder = totalAmount % employees.Count;
        var resolvedComment = string.IsNullOrWhiteSpace(comment)
            ? "Инвентаризация"
            : comment;

        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
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
