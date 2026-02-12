using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Raport;
using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Host.Services.Photo;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Host.Services;

public class GrpcRaportService(
    AppDbContext db,
    TelegramService telegramService,
    PhotoSessionStore photoSessionStore,
    IOptions<PhotoOptions> photoOptions,
    SalaryOperationsService salaryOperationsService,
    SafeOperationsService safeOperationsService,
    SafeUpdatesNotifier safeUpdatesNotifier,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Raport.GrpcRaportService.GrpcRaportServiceBase
{
    private const int InitialCash = 1000;

    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;
    private readonly PhotoSessionStore _photoSessionStore = photoSessionStore;
    private readonly PhotoOptions _photoOptions = photoOptions.Value;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly SafeUpdatesNotifier _safeUpdatesNotifier = safeUpdatesNotifier;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    private sealed record ParsedEmployee(Guid Id, EmployeeRaportSalary Raw);

    private static BoolResponse Fail(string message) => new() { Success = false, Message = message };

    public override async Task<BoolResponse> SendRaport(GrpcRaportRequest request, ServerCallContext context)
    {
        var basicValidation = ValidateRequestBasics(request);
        if (basicValidation != null)
        {
            return basicValidation;
        }

        var parseResult = TryParseEmployees(request.Employees, out var parsedEmployees);
        if (parseResult != null)
        {
            return parseResult;
        }

        var totalMinusEntered = parsedEmployees.Sum(e => e.Raw.Minus);
        var currentSafe = await _safeOperationsService.GetCurrentSafeAsync(context.CancellationToken);
        var (cashDelta, cashNet, safeDelta, totalMinusExpected) = CalculateDeltas(request, currentSafe);

        if (totalMinusEntered != totalMinusExpected)
        {
            return Fail($"Сумма минусов должна быть равна {totalMinusExpected}.");
        }

        var employees = await LoadEmployeesAsync(parsedEmployees, context.CancellationToken);
        if (employees == null)
        {
            return Fail("Сотрудники не найдены.");
        }

        var scope = _scopeAccessor.Current ?? throw new InvalidOperationException("Telegram scope is not available.");

        var runningSafe = currentSafe;
        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            var raport = CreateRaportEntity(request);
            _db.Raports.Add(raport);

            var raportEmployeeSummaries = await CreateRaportEmployeesAsync(
                parsedEmployees,
                employees,
                raport,
                scope,
                context.CancellationToken);

            runningSafe = await ApplySafeOperationsAsync(
                cashNet,
                safeDelta,
                currentSafe,
                scope,
                context.CancellationToken);

            await ApplyNonCashAsync(request, context.CancellationToken);

            await SendPhotosIfNeededAsync(request, scope, context.CancellationToken);

            var revenue = cashNet + request.FactNonCash;
            var totalSalary = raportEmployeeSummaries.Sum(s => s.Salary);
            var total = revenue - totalSalary;
            var cashDiscrepancy = cashDelta < 0 ? cashDelta : 0;

            await _telegramService.SendRaportAsync(
                raport,
                raportEmployeeSummaries,
                currentSafe,
                revenue,
                totalSalary,
                total,
                cashDiscrepancy,
                safeDelta,
                scope,
                context.CancellationToken);

            await _db.SaveChangesAsync(context.CancellationToken);
        }, context.CancellationToken);

        _safeUpdatesNotifier.Publish(runningSafe);

        if (!string.IsNullOrWhiteSpace(request.PhotoSessionKey))
        {
            _photoSessionStore.RemoveSession(request.PhotoSessionKey);
        }

        return new BoolResponse { Success = true, Message = "Смена закрыта." };
    }

    private static BoolResponse? ValidateRequestBasics(GrpcRaportRequest request)
    {
        if (request.Employees.Count == 0)
        {
            return Fail("Не выбраны сотрудники.");
        }

        if (request.Employees.Count > 3)
        {
            return Fail("Максимум 3 сотрудника.");
        }

        int totalHours = request.Employees.Sum(e => e.Hours);
        if (totalHours > 12)
        {
            return Fail("Суммарно больше 12 часов.");
        }

        return null;
    }

    private static BoolResponse? TryParseEmployees(
        IReadOnlyCollection<EmployeeRaportSalary> employees,
        out List<ParsedEmployee> parsed)
    {
        parsed = employees
            .Select(e => new ParsedEmployee(
                Guid.TryParse(e.EmployeeId, out var id) ? id : Guid.Empty,
                e))
            .ToList();

        if (parsed.Any(e => e.Id == Guid.Empty))
        {
            return Fail("Некорректный employee_id.");
        }

        if (parsed.Any(e => e.Raw.Minus < 0))
        {
            return Fail("Минус не может быть отрицательным.");
        }

        return null;
    }

    private static (int cashDelta, int cashNet, int safeDelta, int totalMinusExpected) CalculateDeltas(
        GrpcRaportRequest request,
        int currentSafe)
    {
        var cashDelta = (request.FactCash + request.FactNonCash)
                        - (request.ProgramCash + request.ProgramNonCash);

        var cashNet = request.FactCash - InitialCash;
        var safeDelta = request.FactSafe - currentSafe;
        var totalMinusExpected = (cashDelta < 0 ? -cashDelta : 0) + (safeDelta < 0 ? -safeDelta : 0);

        return (cashDelta, cashNet, safeDelta, totalMinusExpected);
    }

    private async Task<Dictionary<Guid, EmployeeEntity>?> LoadEmployeesAsync(
        IEnumerable<ParsedEmployee> parsedEmployees,
        CancellationToken ct)
    {
        var ids = parsedEmployees.Select(e => e.Id).Distinct().ToList();

        var employees = await _db.Employees
            .Where(e => ids.Contains(e.Id))
            .AsNoTracking()
            .ToListAsync(ct);

        if (employees.Count != ids.Count)
        {
            return null;
        }

        return employees.ToDictionary(e => e.Id);
    }

    private static RaportEntity CreateRaportEntity(GrpcRaportRequest request) =>
        new()
        {
            FactCash = request.FactCash,
            FactNonCash = request.FactNonCash,
            ProgramCash = request.ProgramCash,
            ProgramNonCash = request.ProgramNonCash,
            FactSafe = request.FactSafe,
            WhyMinus = request.WhyMinus ?? string.Empty,
            PhotoSessionKey = string.IsNullOrWhiteSpace(request.PhotoSessionKey)
                ? null
                : request.PhotoSessionKey
        };

    private async Task<List<TelegramService.RaportEmployeeSummary>> CreateRaportEmployeesAsync(
        IEnumerable<ParsedEmployee> parsedEmployees,
        IReadOnlyDictionary<Guid, EmployeeEntity> employees,
        RaportEntity raport,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var raportEmployeeSummaries = new List<TelegramService.RaportEmployeeSummary>();

        foreach (var entry in parsedEmployees)
        {
            var employee = employees[entry.Id];
            int salary = (employee.HourlyRate * entry.Raw.Hours) - entry.Raw.Minus;

            var raportEmployee = new RaportEmployeeEntity
            {
                RaportId = raport.Id,
                EmployeeId = entry.Id,
                Hours = entry.Raw.Hours,
                Minus = entry.Raw.Minus,
                Salary = salary
            };

            _db.RaportEmployees.Add(raportEmployee);
            raportEmployeeSummaries.Add(new TelegramService.RaportEmployeeSummary(
                employee.Name,
                raportEmployee.Hours,
                raportEmployee.Minus,
                raportEmployee.Salary));

            if (salary == 0)
            {
                continue;
            }

            var salaryType = salary >= 0 ? SalaryOperationType.Regular : SalaryOperationType.Fine;
            await _salaryOperationsService.ApplySalaryOperationAsync(
                entry.Id,
                salary,
                salaryType,
                "Закрытие смены",
                scope,
                ct);
        }

        return raportEmployeeSummaries;
    }

    private async Task<int> ApplySafeOperationsAsync(
        int cashNet,
        int safeDelta,
        int currentSafe,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var runningSafe = currentSafe;

        if (cashNet != 0)
        {
            runningSafe = await _safeOperationsService.ApplySafeOperationAsync(
                cashNet,
                "Пополнение сейфа (касса)",
                runningSafe,
                scope,
                ct);
        }

        if (safeDelta != 0)
        {
            runningSafe = await _safeOperationsService.ApplySafeOperationAsync(
                safeDelta,
                "Уравнивание расхождения",
                runningSafe,
                scope,
                ct);
        }

        return runningSafe;
    }

    private Task ApplyNonCashAsync(GrpcRaportRequest request, CancellationToken ct)
    {
        if (request.FactNonCash == 0)
        {
            return Task.CompletedTask;
        }

        var nonCashType = request.FactNonCash > 0
            ? NonCashOperationType.Coming
            : NonCashOperationType.Expense;

        _db.NonCashOperations.Add(new NonCashOperationEntity
        {
            Amount = Math.Abs(request.FactNonCash),
            Type = nonCashType,
            Comment = "Выручка (безнал)"
        });

        return Task.CompletedTask;
    }

    private async Task SendPhotosIfNeededAsync(
        GrpcRaportRequest request,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (!request.SendPhoto)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.PhotoSessionKey))
        {
            throw new InvalidOperationException("Не указан ключ фото.");
        }

        if (!_photoSessionStore.TryGetSession(
                request.PhotoSessionKey,
                TimeSpan.FromSeconds(_photoOptions.SessionTtlSeconds),
                out var fileIds))
        {
            throw new InvalidOperationException("Фото не найдены или ключ истек.");
        }

        await _telegramService.SendRaportPhotosAsync(fileIds, scope, ct);
    }

}
