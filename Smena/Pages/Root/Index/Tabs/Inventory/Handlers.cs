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
        // Битые id отклоняем целиком — молчаливый отброс перекладывал их долю
        // недостачи на остальных сотрудников.
        var ids = new List<Guid>();
        foreach (var raw in Inventory.EmployeeIds)
        {
            if (!Guid.TryParse(raw, out var id) || id == Guid.Empty)
            {
                ErrorMessage = "Некорректный сотрудник в списке.";
                return RedirectToPage(BuildInventoryRouteValues());
            }

            ids.Add(id);
        }

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

        var comment = string.IsNullOrWhiteSpace(Inventory.Comment)
            ? "ROOT: операция инвентаризации"
            : $"ROOT: {Inventory.Comment}";

        // Деление суммы, валидации и блокировки — в InventoryOperationsService
        // (раньше handler дублировал алгоритм почти построчно и мог разъехаться
        // с gRPC-потоком).
        var scope = _telegramService.CreateScope();
        try
        {
            var result = await _inventoryOperationsService.SendInventoryAsync(
                Inventory.TotalAmount, ids, comment, scope, ct);

            if (result.Success)
            {
                StatusMessage = "Инвентаризация применена.";
            }
            else
            {
                ErrorMessage = result.Message;
            }
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
            // Unspecified = ввод из datetime-local, трактуем в бизнес-таймзоне (env TZ).
            return Host.Services.BusinessTime.ToUtc(dateTime);
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
