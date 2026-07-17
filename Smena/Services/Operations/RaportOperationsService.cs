using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Photo;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Host.Services.Operations;

public class RaportOperationsService(
    AppDbContext db,
    TelegramService telegramService,
    PhotoSessionStore photoSessionStore,
    IOptions<PhotoOptions> photoOptions,
    SalaryOperationsService salaryOperationsService,
    SafeOperationsService safeOperationsService,
    NonCashOperationsService nonCashOperationsService)
{
    private const int InitialCash = 1000;

    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;
    private readonly PhotoSessionStore _photoSessionStore = photoSessionStore;
    private readonly PhotoOptions _photoOptions = photoOptions.Value;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly NonCashOperationsService _nonCashOperationsService = nonCashOperationsService;

    public sealed record RaportEmployeeInput(Guid EmployeeId, int Hours, int Minus);

    public sealed record RaportInput(
        int FactCash,
        int FactNonCash,
        int ProgramCash,
        int ProgramNonCash,
        int FactSafe,
        string? WhyMinus,
        bool SendPhoto,
        string? PhotoSessionKey,
        IReadOnlyList<RaportEmployeeInput> Employees);

    public async Task<OperationResult> SendRaportAsync(
        RaportInput input,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var validation = ValidateBasics(input);
        if (!validation.Success)
        {
            return validation;
        }

        var employeeIds = input.Employees.Select(e => e.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .AsNoTracking()
            .ToDictionaryAsync(e => e.Id, ct);

        if (employees.Count != employeeIds.Count)
        {
            return OperationResult.Fail("Сотрудники не найдены.");
        }

        int? finalSafe = null;

        var result = await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            // Блокировки в общем порядке приложения (сейф → сотрудники по Id),
            // и только под ними читаются балансы. Раньше currentSafe читался ДО
            // транзакции: параллельная операция по сейфу в этом окне делала
            // «Уравнивание расхождения» неверным — необратимая порча append-only
            // журнала, а сумма минусов валидировалась против устаревшего значения.
            await AdvisoryLocks.AcquireSafeAsync(_db, ct);
            await AdvisoryLocks.AcquireEmployeeSalariesAsync(_db, employeeIds, ct);

            var currentSafe = await _safeOperationsService.GetCurrentSafeAsync(ct);
            var (cashDelta, cashNet, safeDelta, totalMinusExpected) = CalculateDeltas(input, currentSafe);

            var totalMinusEntered = input.Employees.Sum(e => e.Minus);
            if (totalMinusEntered != totalMinusExpected)
            {
                return OperationResult.Fail($"Сумма минусов должна быть равна {totalMinusExpected}.");
            }

            var minusValidation = await ValidateEmployeeMinusRulesAsync(input.Employees, employees, ct);
            if (!minusValidation.Success)
            {
                return minusValidation;
            }

            var raport = new RaportEntity
            {
                FactCash = input.FactCash,
                FactNonCash = input.FactNonCash,
                ProgramCash = input.ProgramCash,
                ProgramNonCash = input.ProgramNonCash,
                FactSafe = input.FactSafe,
                WhyMinus = input.WhyMinus ?? string.Empty,
                PhotoSessionKey = string.IsNullOrWhiteSpace(input.PhotoSessionKey)
                    ? null
                    : input.PhotoSessionKey
            };

            _db.Raports.Add(raport);

            var summaries = await CreateRaportEmployeesAsync(
                input.Employees, employees, raport, scope, ct);

            if (cashNet != 0 || safeDelta != 0)
            {
                finalSafe = await ApplySafeOperationsAsync(cashNet, safeDelta, currentSafe, scope, ct);
            }

            ApplyNonCash(input.FactNonCash);
            await SendPhotosIfNeededAsync(input.SendPhoto, input.PhotoSessionKey, scope, ct);

            var revenue = cashNet + input.FactNonCash;
            var totalSalary = summaries.Sum(s => s.Salary);
            var total = revenue - totalSalary;
            var cashDiscrepancy = cashDelta < 0 ? cashDelta : 0;

            await _telegramService.SendRaportAsync(
                raport, summaries, currentSafe, revenue,
                totalSalary, total, cashDiscrepancy, safeDelta,
                scope, ct);

            await _db.SaveChangesAsync(ct);
            return OperationResult.Ok("Смена закрыта.");
        }, ct);

        // Фото-сессию удаляем только при успехе: при отказе валидации кассир
        // исправляет данные и повторяет отправку без повторного запроса фото.
        if (result.Success && !string.IsNullOrWhiteSpace(input.PhotoSessionKey))
        {
            _photoSessionStore.RemoveSession(input.PhotoSessionKey);
        }

        // Публикация нового баланса подписчикам — строго после коммита.
        if (result.Success && finalSafe.HasValue)
        {
            _safeOperationsService.PublishSafeUpdate(finalSafe.Value);
        }

        return result;
    }

    private static OperationResult ValidateBasics(RaportInput input)
    {
        if (input.Employees.Count == 0)
        {
            return OperationResult.Fail("Не выбраны сотрудники.");
        }

        if (input.Employees.Count > 3)
        {
            return OperationResult.Fail("Максимум 3 сотрудника.");
        }

        if (input.Employees.Sum(e => e.Hours) > 12)
        {
            return OperationResult.Fail("Суммарно больше 12 часов.");
        }

        if (input.Employees.Any(e => e.EmployeeId == Guid.Empty))
        {
            return OperationResult.Fail("Некорректный employee_id.");
        }

        if (input.Employees.Any(e => e.Minus < 0))
        {
            return OperationResult.Fail("Минус не может быть отрицательным.");
        }

        // 0 часов — легально (сотрудник мог не выходить на смену, но, например,
        // взять напиток), а вот отрицательные часы превращались в штраф
        // ставка×часы и обходили суммарный лимит (−5 + 17 = 12).
        if (input.Employees.Any(e => e.Hours < 0))
        {
            return OperationResult.Fail("Часы не могут быть отрицательными.");
        }

        return OperationResult.Ok();
    }

    private static (int cashDelta, int cashNet, int safeDelta, int totalMinusExpected) CalculateDeltas(
        RaportInput input, int currentSafe)
    {
        var cashDelta = (input.FactCash + input.FactNonCash)
                        - (input.ProgramCash + input.ProgramNonCash);
        var cashNet = input.FactCash - InitialCash;
        var safeDelta = input.FactSafe - currentSafe;
        var totalMinusExpected = (cashDelta < 0 ? -cashDelta : 0) + (safeDelta < 0 ? -safeDelta : 0);

        return (cashDelta, cashNet, safeDelta, totalMinusExpected);
    }

    private async Task<OperationResult> ValidateEmployeeMinusRulesAsync(
        IReadOnlyList<RaportEmployeeInput> entries,
        IReadOnlyDictionary<Guid, EmployeeEntity> employees,
        CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            var currentSalary = await _salaryOperationsService.GetCurrentSalaryAsync(entry.EmployeeId, ct);
            if (currentSalary >= 0)
            {
                continue;
            }

            var employee = employees[entry.EmployeeId];
            var maxMinus = employee.HourlyRate * entry.Hours;

            if (entry.Minus <= maxMinus)
            {
                continue;
            }

            return OperationResult.Fail(
                $"Для сотрудника {employee.Name} при отрицательной ЗП ({currentSalary} руб.) " +
                $"минус не может превышать {maxMinus} руб.");
        }

        return OperationResult.Ok();
    }

    private async Task<List<TelegramService.RaportEmployeeSummary>> CreateRaportEmployeesAsync(
        IReadOnlyList<RaportEmployeeInput> entries,
        IReadOnlyDictionary<Guid, EmployeeEntity> employees,
        RaportEntity raport,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var summaries = new List<TelegramService.RaportEmployeeSummary>();

        foreach (var entry in entries)
        {
            var employee = employees[entry.EmployeeId];
            int salary = (employee.HourlyRate * entry.Hours) - entry.Minus;

            var raportEmployee = new RaportEmployeeEntity
            {
                RaportId = raport.Id,
                EmployeeId = entry.EmployeeId,
                Hours = entry.Hours,
                Minus = entry.Minus,
                Salary = salary
            };

            _db.RaportEmployees.Add(raportEmployee);
            summaries.Add(new TelegramService.RaportEmployeeSummary(
                employee.Name, raportEmployee.Hours, raportEmployee.Minus, raportEmployee.Salary));

            if (salary == 0)
            {
                continue;
            }

            var salaryType = salary >= 0 ? SalaryOperationType.Regular : SalaryOperationType.Fine;
            await _salaryOperationsService.ApplySalaryOperationAsync(
                entry.EmployeeId, salary, salaryType, "Закрытие смены", scope, ct);
        }

        return summaries;
    }

    /// <returns>Итоговый баланс сейфа — вызывающий публикует его после коммита.</returns>
    private async Task<int> ApplySafeOperationsAsync(
        int cashNet, int safeDelta, int currentSafe,
        TelegramMessageScope scope, CancellationToken ct)
    {
        var runningSafe = currentSafe;

        if (cashNet != 0)
        {
            runningSafe = await _safeOperationsService.ApplySafeOperationAsync(
                cashNet, "Пополнение сейфа (касса)", runningSafe, scope, ct);
        }

        if (safeDelta != 0)
        {
            runningSafe = await _safeOperationsService.ApplySafeOperationAsync(
                safeDelta, "Уравнивание расхождения", runningSafe, scope, ct);
        }

        return runningSafe;
    }

    private void ApplyNonCash(int factNonCash)
    {
        if (factNonCash == 0)
        {
            return;
        }

        _nonCashOperationsService.AddOperation(factNonCash, "Выручка (безнал)");
    }

    private async Task SendPhotosIfNeededAsync(
        bool sendPhoto, string? photoSessionKey,
        TelegramMessageScope scope, CancellationToken ct)
    {
        if (!sendPhoto)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(photoSessionKey))
        {
            throw new InvalidOperationException("Не указан ключ фото.");
        }

        if (!_photoSessionStore.TryGetSession(
                photoSessionKey,
                TimeSpan.FromSeconds(_photoOptions.SessionTtlSeconds),
                out var fileIds))
        {
            throw new InvalidOperationException("Фото не найдены или ключ истек.");
        }

        await _telegramService.SendRaportPhotosAsync(fileIds, scope, ct);
    }
}
