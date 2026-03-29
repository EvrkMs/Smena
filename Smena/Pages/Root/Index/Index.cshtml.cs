using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Host.Services.RootPanel;
using Host.Services.Telegram;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Host.Pages.Root;

public partial class IndexModel(
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

    public IActionResult OnPostLogout()
    {
        var refreshToken = Request.Cookies[RootPanelAuthMiddleware.RefreshCookieName];
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            _authService.RevokeByRefreshToken(refreshToken);
        }

        RootPanelAuthMiddleware.ClearAuthCookies(Response);
        return RedirectToPage("/Root/Login");
    }
}
