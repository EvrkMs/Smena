using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Host.Pages.Root;

public partial class IndexModel
{
    public async Task<IActionResult> OnPostForceAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(Force.EmployeeId, out var employeeId))
        {
            ErrorMessage = "Некорректный сотрудник.";
            return Redirect("/root?tab=force");
        }

        if (Force.Amount <= 0)
        {
            ErrorMessage = "Сумма должна быть больше 0.";
            return Redirect("/root?tab=force");
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);

        if (employee == null)
        {
            ErrorMessage = "Сотрудник не найден.";
            return Redirect("/root?tab=force");
        }

        var scope = _telegramService.CreateScope();
        try
        {
            int? updatedSafe = null;
            await TransactionHelper.ExecuteAsync(_db, async () =>
            {
                var type = Force.IsSalary ? SalaryOperationType.Pay : SalaryOperationType.Advance;
                var comment = string.IsNullOrWhiteSpace(Force.Comment)
                    ? (Force.IsSalary ? "ROOT: принудительная выплата зарплаты" : "ROOT: принудительный аванс")
                    : $"ROOT: {Force.Comment}";

                await _salaryOperationsService.ApplySalaryOperationAsync(
                    employeeId,
                    -Force.Amount,
                    type,
                    comment,
                    scope,
                    ct);

                if (Force.IsNonCash)
                {
                    _db.NonCashOperations.Add(new NonCashOperationEntity
                    {
                        Amount = Force.Amount,
                        Type = NonCashOperationType.Expense,
                        Comment = $"{employee.Name}: {comment}"
                    });
                }
                else
                {
                    updatedSafe = await _safeOperationsService.ApplySafeOperationAsync(
                        -Force.Amount,
                        $"{employee.Name}: {comment}",
                        scope,
                        ct);
                }

                await _db.SaveChangesAsync(ct);
            }, ct);

            if (updatedSafe.HasValue)
            {
                _safeUpdatesNotifier.Publish(updatedSafe.Value);
            }

            StatusMessage = "Принудительная выплата применена.";
        }
        catch (Exception ex)
        {
            await scope.RollbackAsync(CancellationToken.None);
            ErrorMessage = ex.Message;
        }

        return Redirect("/root?tab=force");
    }
}
