using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Host.Pages.Root;

public partial class IndexModel
{
    public async Task<IActionResult> OnPostSalaryAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(Salary.EmployeeId, out var employeeId))
        {
            ErrorMessage = "Некорректный сотрудник.";
            return RedirectToPage(new { tab = "salary" });
        }

        if (Salary.Amount <= 0)
        {
            ErrorMessage = "Сумма должна быть больше 0.";
            return RedirectToPage(new { tab = "salary" });
        }

        if (!Enum.IsDefined(Salary.Type))
        {
            ErrorMessage = "Некорректный тип операции по зарплате.";
            return RedirectToPage(new { tab = "salary" });
        }

        var isPositiveType = Salary.Type is SalaryOperationType.Regular or SalaryOperationType.Bonus;
        if (Salary.IsIncrease && !isPositiveType)
        {
            ErrorMessage = "Выбранный тип доступен только для списания.";
            return RedirectToPage(new { tab = "salary" });
        }

        if (!Salary.IsIncrease && isPositiveType)
        {
            ErrorMessage = "Выбранный тип доступен только для начисления.";
            return RedirectToPage(new { tab = "salary" });
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);

        if (employee == null)
        {
            ErrorMessage = "Сотрудник не найден.";
            return RedirectToPage(new { tab = "salary" });
        }

        var signedAmount = Salary.IsIncrease ? Salary.Amount : -Salary.Amount;
        var comment = string.IsNullOrWhiteSpace(Salary.Comment)
            ? $"ROOT: ручная операция по зарплате ({Salary.Type})"
            : $"ROOT: {Salary.Comment}";

        var scope = _telegramService.CreateScope();
        try
        {
            await TransactionHelper.ExecuteAsync(_db, async () =>
            {
                await _salaryOperationsService.ApplySalaryOperationAsync(
                    employeeId,
                    signedAmount,
                    Salary.Type,
                    comment,
                    scope,
                    ct);

                await _db.SaveChangesAsync(ct);
            }, ct);

            StatusMessage = "Операция по зарплате применена.";
        }
        catch (Exception ex)
        {
            await scope.RollbackAsync(CancellationToken.None);
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { tab = "salary" });
    }
}
