using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Host.Services.RootPanel;
using Host.Services.Telegram;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Host.Pages.Root;

public class IndexModel(
    AppDbContext db,
    TelegramService telegramService,
    SalaryOperationsService salaryOperationsService,
    SafeOperationsService safeOperationsService,
    SafeUpdatesNotifier safeUpdatesNotifier,
    IRootPanelAuthService authService,
    IConfiguration configuration) : PageModel
{
    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly SafeUpdatesNotifier _safeUpdatesNotifier = safeUpdatesNotifier;
    private readonly IRootPanelAuthService _authService = authService;
    private readonly TimeSpan _businessUtcOffset = TimeSpan.FromHours(
        configuration.GetValue<int?>("RootPanel:TimeZoneOffsetHours") ?? 3);

    public sealed record EmployeeSnapshot(
        Guid Id,
        string Name,
        int HourlyRate,
        int CurrentSalary,
        int PeriodHours);

    public sealed class ForcePayoutInput
    {
        public string EmployeeId { get; set; } = string.Empty;
        public int Amount { get; set; }
        public bool IsSalary { get; set; }
        public bool IsNonCash { get; set; }
        public string Comment { get; set; } = string.Empty;
    }

    public sealed class InventoryInput
    {
        public int TotalAmount { get; set; }
        public string Comment { get; set; } = string.Empty;
        public List<string> EmployeeIds { get; set; } = [];
    }

    public sealed class SalaryAdjustInput
    {
        public string EmployeeId { get; set; } = string.Empty;
        public int Amount { get; set; }
        public bool IsIncrease { get; set; }
        public SalaryOperationType Type { get; set; } = SalaryOperationType.Fine;
        public string Comment { get; set; } = string.Empty;
    }

    [BindProperty]
    public ForcePayoutInput Force { get; set; } = new();

    [BindProperty]
    public InventoryInput Inventory { get; set; } = new();

    [BindProperty]
    public SalaryAdjustInput Salary { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public DateTime? InventoryFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? InventoryTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? InventoryHours { get; set; }

    public List<EmployeeSnapshot> Employees { get; private set; } = [];
    public List<EmployeeSnapshot> InventoryEmployees { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "force";

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadEmployeesAsync(ct);
    }

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
            return Redirect(BuildInventoryRedirectUrl());
        }

        if (ids.Count == 0)
        {
            ErrorMessage = "Выберите хотя бы одного сотрудника.";
            return Redirect(BuildInventoryRedirectUrl());
        }

        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        if (employees.Count == 0)
        {
            ErrorMessage = "Сотрудники не найдены.";
            return Redirect(BuildInventoryRedirectUrl());
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

        return Redirect(BuildInventoryRedirectUrl());
    }

    public async Task<IActionResult> OnPostSalaryAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(Salary.EmployeeId, out var employeeId))
        {
            ErrorMessage = "Некорректный сотрудник.";
            return Redirect("/root?tab=salary");
        }

        if (Salary.Amount <= 0)
        {
            ErrorMessage = "Сумма должна быть больше 0.";
            return Redirect("/root?tab=salary");
        }

        if (!Enum.IsDefined(Salary.Type))
        {
            ErrorMessage = "Некорректный тип операции по зарплате.";
            return Redirect("/root?tab=salary");
        }

        var isPositiveType = Salary.Type is SalaryOperationType.Regular or SalaryOperationType.Bonus;
        if (Salary.IsIncrease && !isPositiveType)
        {
            ErrorMessage = "Выбранный тип доступен только для списания.";
            return Redirect("/root?tab=salary");
        }

        if (!Salary.IsIncrease && isPositiveType)
        {
            ErrorMessage = "Выбранный тип доступен только для начисления.";
            return Redirect("/root?tab=salary");
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);

        if (employee == null)
        {
            ErrorMessage = "Сотрудник не найден.";
            return Redirect("/root?tab=salary");
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

        return Redirect("/root?tab=salary");
    }

    public IActionResult OnPostLogout()
    {
        var refreshToken = Request.Cookies[RootPanelAuthMiddleware.RefreshCookieName];
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            _authService.RevokeByRefreshToken(refreshToken);
        }

        RootPanelAuthMiddleware.ClearAuthCookies(Response);
        return Redirect("/root/login");
    }

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

        // If date-only query values were passed (yyyy-MM-dd), interpret "to" as end-of-day.
        if (TryGetDateOnlyQueryValue("InventoryFrom", out var fromDateOnly))
        {
            fromLocal = fromDateOnly;
        }

        if (TryGetDateOnlyQueryValue("InventoryTo", out var toDateOnly))
        {
            toLocal = toDateOnly.Date.AddDays(1).AddTicks(-1);
        }

        // If both boundaries are exactly at 00:00 and span multiple days, users usually mean full days.
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

    private string BuildInventoryRedirectUrl()
    {
        var query = new List<string> { "tab=inventory" };

        if (InventoryFrom.HasValue)
        {
            query.Add($"InventoryFrom={Uri.EscapeDataString(InventoryFrom.Value.ToString("yyyy-MM-ddTHH:mm"))}");
        }

        if (InventoryTo.HasValue)
        {
            query.Add($"InventoryTo={Uri.EscapeDataString(InventoryTo.Value.ToString("yyyy-MM-ddTHH:mm"))}");
        }

        if (InventoryHours.HasValue && InventoryHours.Value > 0)
        {
            query.Add($"InventoryHours={InventoryHours.Value}");
        }

        return $"/root?{string.Join("&", query)}";
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
