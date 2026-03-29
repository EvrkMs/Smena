using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Host.Pages.Root;

public partial class IndexModel
{
    public async Task<IActionResult> OnPostInventoryAsync(CancellationToken ct)
    {
        var ids = Inventory.EmployeeIds
            .Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (Inventory.TotalAmount <= 0)
        {
            ErrorMessage = "Сумма инвентаризации должна быть больше 0.";
            return RedirectToPage(BuildInventoryRouteValues());
        }

        if (ids.Count == 0)
        {
            ErrorMessage = "Выберите хотя бы одного сотрудника.";
            return RedirectToPage(BuildInventoryRouteValues());
        }

        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        if (employees.Count == 0)
        {
            ErrorMessage = "Сотрудники не найдены.";
            return RedirectToPage(BuildInventoryRouteValues());
        }

        var perEmployee = Inventory.TotalAmount / employees.Count;
        var remainder = Inventory.TotalAmount % employees.Count;
        var comment = string.IsNullOrWhiteSpace(Inventory.Comment)
            ? "ROOT: операция инвентаризации"
            : $"ROOT: {Inventory.Comment}";

        var scope = _telegramService.CreateScope();
        try
        {
            await TransactionHelper.ExecuteAsync(_db, async () =>
            {
                foreach (var employee in employees)
                {
                    var amount = perEmployee + (remainder > 0 ? 1 : 0);
                    if (remainder > 0)
                    {
                        remainder--;
                    }

                    await _salaryOperationsService.ApplySalaryOperationAsync(
                        employee.Id,
                        -amount,
                        SalaryOperationType.Inventory,
                        comment,
                        scope,
                        ct);
                }

                await _db.SaveChangesAsync(ct);
            }, ct);

            StatusMessage = "Инвентаризация применена.";
        }
        catch (Exception ex)
        {
            await scope.RollbackAsync(CancellationToken.None);
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(BuildInventoryRouteValues());
    }

    private async Task<List<EmployeeSnapshot>> BuildInventoryEmployeesAsync(
        IReadOnlyList<EmployeeSnapshot> allEmployees,
        CancellationToken ct)
    {
        if (allEmployees.Count == 0)
        {
            return [];
        }

        var fromLocal = InventoryFrom;
        var toLocal = InventoryTo;

        if (TryGetDateOnlyQueryValue("InventoryFrom", out var fromDateOnly))
        {
            fromLocal = fromDateOnly;
        }

        if (TryGetDateOnlyQueryValue("InventoryTo", out var toDateOnly))
        {
            toLocal = toDateOnly.Date.AddDays(1).AddTicks(-1);
        }

        if (fromLocal.HasValue &&
            toLocal.HasValue &&
            fromLocal.Value.TimeOfDay == TimeSpan.Zero &&
            toLocal.Value.TimeOfDay == TimeSpan.Zero &&
            fromLocal.Value.Date < toLocal.Value.Date)
        {
            toLocal = toLocal.Value.Date.AddDays(1).AddTicks(-1);
        }

        if (fromLocal.HasValue && toLocal.HasValue && fromLocal.Value > toLocal.Value)
        {
            ErrorMessage = "Некорректный диапазон даты/времени фильтра инвентаризации.";
            return [];
        }

        var usePeriodFilter = InventoryFrom.HasValue || InventoryTo.HasValue || (InventoryHours.HasValue && InventoryHours.Value > 0);
        if (!usePeriodFilter)
        {
            return [.. allEmployees];
        }

        var from = ToUtc(fromLocal);
        var to = ToUtc(toLocal);

        var hoursByEmployee = await _db.RaportEmployees
            .AsNoTracking()
            .Where(x => !from.HasValue || x.Raport.CreatedAt >= from.Value)
            .Where(x => !to.HasValue || x.Raport.CreatedAt <= to.Value)
            .GroupBy(x => x.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Hours = g.Sum(x => x.Hours)
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Hours, ct);

        var filtered = allEmployees
            .Select(e => e with { PeriodHours = hoursByEmployee.GetValueOrDefault(e.Id, 0) })
            .ToList();

        if (InventoryHours.HasValue && InventoryHours.Value > 0)
        {
            filtered = filtered
                .Where(x => x.PeriodHours >= InventoryHours.Value)
                .ToList();
        }

        return filtered;
    }

    private object BuildInventoryRouteValues()
    {
        var values = new Dictionary<string, object?> { ["tab"] = "inventory" };

        if (InventoryFrom.HasValue)
        {
            values["InventoryFrom"] = InventoryFrom.Value.ToString("yyyy-MM-ddTHH:mm");
        }

        if (InventoryTo.HasValue)
        {
            values["InventoryTo"] = InventoryTo.Value.ToString("yyyy-MM-ddTHH:mm");
        }

        if (InventoryHours.HasValue && InventoryHours.Value > 0)
        {
            values["InventoryHours"] = InventoryHours.Value;
        }

        return values;
    }

    private DateTime? ToUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var dateTime = value.Value;
        if (dateTime.Kind == DateTimeKind.Utc)
        {
            return dateTime;
        }

        if (dateTime.Kind == DateTimeKind.Local)
        {
            return dateTime.ToUniversalTime();
        }

        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            return new DateTimeOffset(dateTime, _businessUtcOffset).UtcDateTime;
        }

        return dateTime.ToUniversalTime();
    }

    private bool TryGetDateOnlyQueryValue(string key, out DateTime value)
    {
        value = default;

        if (!Request.Query.TryGetValue(key, out var rawValues))
        {
            return false;
        }

        var raw = rawValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('T'))
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        value = parsed.Date;
        return true;
    }
}
