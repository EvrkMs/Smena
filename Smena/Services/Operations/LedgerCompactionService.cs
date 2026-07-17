using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Operations;

/// <summary>
/// Финализация журналов сейфа и ЗП (решение владельца: не отдельная таблица
/// снапшотов, а схлопывание истории «на месте» — постоянная история не нужна,
/// отдельное хранение избыточно; архивом остаются Telegram-чаты).
///
/// Всё, что СТАРШЕ первого числа ПРОШЛОГО месяца (в бизнес-таймзоне),
/// превращается в одну запись «Финализация истории до …»: по сейфу — одна,
/// по ЗП — на каждого сотрудника. Текущий и прошлый месяц остаются полной
/// историей. SUM-балансы не меняются — меняется только число строк, по
/// которым они считаются (лечит вечный рост журналов и деградацию каждого
/// чтения баланса).
///
/// Запускается при старте (миграции к этому моменту уже применены — Program
/// ждёт их до app.Run) и далее раз в сутки; фактическая работа происходит
/// только когда граница месяца сдвинулась и появились записи старше неё.
/// Агрегат датируется на секунду раньше границы, поэтому при следующей
/// финализации он схлопнется вместе с новой «постаревшей» историей.
/// </summary>
public sealed class LedgerCompactionService(
    IServiceScopeFactory scopeFactory,
    ILogger<LedgerCompactionService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<LedgerCompactionService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSafeguardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSafeguardedAsync(stoppingToken);
        }
    }

    private async Task RunSafeguardedAsync(CancellationToken ct)
    {
        try
        {
            await CompactAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LedgerCompaction] Compaction failed — will retry on next tick");
        }
    }

    private async Task CompactAsync(CancellationToken ct)
    {
        var businessToday = BusinessTime.Now.Date;
        var firstOfPreviousMonth = new DateTime(businessToday.Year, businessToday.Month, 1).AddMonths(-1);
        var cutoffUtc = BusinessTime.ToUtc(firstOfPreviousMonth);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await CompactSafeAsync(db, cutoffUtc, ct);
        await CompactSalariesAsync(db, cutoffUtc, ct);
    }

    private async Task CompactSafeAsync(AppDbContext db, DateTime cutoffUtc, CancellationToken ct)
    {
        await TransactionHelper.ExecuteAsync(db, async () =>
        {
            await AdvisoryLocks.AcquireSafeAsync(db, ct);

            var old = db.SafeOperations.Where(o => o.CreatedAt < cutoffUtc);
            var count = await old.CountAsync(ct);
            if (count <= 1)
            {
                return;
            }

            var net = await old.SumAsync(
                o => o.Type == SafeOperationType.Coming ? o.Amount : -o.Amount, ct);
            await old.ExecuteDeleteAsync(ct);

            if (net != 0)
            {
                db.SafeOperations.Add(new SafeOperationEntity
                {
                    Amount = Math.Abs(net),
                    Type = net > 0 ? SafeOperationType.Coming : SafeOperationType.Expense,
                    Comment = $"Финализация истории до {cutoffUtc:yyyy-MM-dd}",
                    CreatedAt = cutoffUtc.AddSeconds(-1)
                });
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[LedgerCompaction] Safe: {Count} records compacted into aggregate {Net}", count, net);
        }, ct);
    }

    private async Task CompactSalariesAsync(AppDbContext db, DateTime cutoffUtc, CancellationToken ct)
    {
        var employeeIds = await db.SalaryOperations
            .AsNoTracking()
            .Where(o => o.CreatedAt < cutoffUtc)
            .Select(o => o.EmployeeId)
            .Distinct()
            .ToListAsync(ct);

        // По сотруднику на транзакцию — блокировка держится коротко и не
        // конфликтует с операциями смены дольше необходимого.
        foreach (var employeeId in employeeIds)
        {
            await TransactionHelper.ExecuteAsync(db, async () =>
            {
                await AdvisoryLocks.AcquireEmployeeSalaryAsync(db, employeeId, ct);

                var old = db.SalaryOperations
                    .Where(o => o.EmployeeId == employeeId && o.CreatedAt < cutoffUtc);
                var count = await old.CountAsync(ct);
                if (count <= 1)
                {
                    return;
                }

                var net = await old.SumAsync(
                    o => o.Type == SalaryOperationType.Regular || o.Type == SalaryOperationType.Bonus
                        ? o.Amount
                        : -o.Amount, ct);
                await old.ExecuteDeleteAsync(ct);

                if (net != 0)
                {
                    db.SalaryOperations.Add(new SalaryOperationEntity
                    {
                        EmployeeId = employeeId,
                        Amount = Math.Abs(net),
                        // Полярность агрегата в формуле баланса: Regular — плюс, Fine — минус.
                        Type = net > 0 ? SalaryOperationType.Regular : SalaryOperationType.Fine,
                        Comment = $"Финализация истории до {cutoffUtc:yyyy-MM-dd}",
                        CreatedAt = cutoffUtc.AddSeconds(-1)
                    });
                }

                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "[LedgerCompaction] Salary {EmployeeId}: {Count} records compacted into aggregate {Net}",
                    employeeId, count, net);
            }, ct);
        }
    }
}
