using Microsoft.EntityFrameworkCore;

namespace Host.Services.Data;

/// <summary>
/// Транзакционные advisory-блокировки Postgres (pg_advisory_xact_lock):
/// снимаются автоматически при commit/rollback, поэтому берутся ТОЛЬКО внутри
/// TransactionHelper.ExecuteAsync. Балансы (сейф, ЗП) считаются суммой по
/// append-only журналам — строки «баланса» не существует, и обычный
/// SELECT ... FOR UPDATE применить не к чему; блокируем логический ресурс.
///
/// Порядок взятия фиксированный для всего приложения: СНАЧАЛА сейф, ПОТОМ
/// сотрудники по возрастанию Id. Нарушение порядка в новом коде = взаимная
/// блокировка двух конкурентных запросов.
/// </summary>
public static class AdvisoryLocks
{
    // Стабильные class-ключи двухчастной блокировки (int, int) — произвольные,
    // но зафиксированные константы приложения.
    private const int SafeClass = 0x536D5361;   // "SmSa"
    private const int SalaryClass = 0x536D5379; // "SmSy"

    public static Task AcquireSafeAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SafeClass}, 0)", ct);

    // Guid.GetHashCode детерминирован (считается из байтов, без рандомизации);
    // коллизия хэшей лишь избыточно сериализует два разных сотрудника — безопасно.
    public static Task AcquireEmployeeSalaryAsync(AppDbContext db, Guid employeeId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SalaryClass}, {employeeId.GetHashCode()})", ct);

    public static async Task AcquireEmployeeSalariesAsync(
        AppDbContext db, IEnumerable<Guid> employeeIds, CancellationToken ct)
    {
        foreach (var id in employeeIds.Distinct().OrderBy(x => x))
        {
            await AcquireEmployeeSalaryAsync(db, id, ct);
        }
    }
}
