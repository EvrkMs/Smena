using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Host.Services.RootPanel;
using Host.Services.Telegram;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Host.Pages.Root;

public class IndexModel(
    AppDbContext db,
    TelegramService telegramService,
    SalaryOperationsService salaryOperationsService,
    SafeOperationsService safeOperationsService,
    SafeUpdatesNotifier safeUpdatesNotifier,
    IRootPanelAuthService authService) : PageModel
{
    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly SafeUpdatesNotifier _safeUpdatesNotifier = safeUpdatesNotifier;
    private readonly IRootPanelAuthService _authService = authService;

    public sealed record EmployeeSnapshot(Guid Id, string Name, int HourlyRate, int CurrentSalary);

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

    [BindProperty]
    public ForcePayoutInput Force { get; set; } = new();

    [BindProperty]
    public InventoryInput Inventory { get; set; } = new();

    public List<EmployeeSnapshot> Employees { get; private set; } = [];

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
                    ? (Force.IsSalary ? "ROOT: Выплата ЗП (override)" : "ROOT: Аванс (override)")
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

            StatusMessage = "Операция выполнена (без ограничений правил).";
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
            return Redirect("/root?tab=inventory");
        }

        if (ids.Count == 0)
        {
            ErrorMessage = "Выберите сотрудников.";
            return Redirect("/root?tab=inventory");
        }

        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        if (employees.Count == 0)
        {
            ErrorMessage = "Сотрудники не найдены.";
            return Redirect("/root?tab=inventory");
        }

        var perEmployee = Inventory.TotalAmount / employees.Count;
        var remainder = Inventory.TotalAmount % employees.Count;
        var comment = string.IsNullOrWhiteSpace(Inventory.Comment)
            ? "ROOT: Инвентаризация"
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

            StatusMessage = "Инвентаризация проведена.";
        }
        catch (Exception ex)
        {
            await scope.RollbackAsync(CancellationToken.None);
            ErrorMessage = ex.Message;
        }

        return Redirect("/root?tab=inventory");
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
                salaries.GetValueOrDefault(e.Id, 0)))
            .ToList();
    }
}
