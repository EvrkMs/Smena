using Host.Services.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Host.Pages.Root;

public partial class IndexModel
{
    private async Task LoadEmployeesAsync(CancellationToken ct)
    {
        var salaries = await _db.SalaryOperations
            .AsNoTracking()
            .GroupBy(x => x.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                CurrentSalary = g.Sum(x => x.Type == SalaryOperationType.Regular || x.Type == SalaryOperationType.Bonus
                    ? x.Amount
                    : -x.Amount)
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.CurrentSalary, ct);

        var employees = await _db.Employees
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        Employees = employees
            .Select(e => new EmployeeSnapshot(
                e.Id,
                e.Name,
                e.HourlyRate,
                salaries.GetValueOrDefault(e.Id, 0),
                0))
            .ToList();

        InventoryEmployees = await BuildInventoryEmployeesAsync(Employees, ct);
    }
}
