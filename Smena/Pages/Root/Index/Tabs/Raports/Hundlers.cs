using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Host.Pages.Root;

public partial class IndexModel
{
    public string ActiveTab { get; set; } = "force";
    // Период отчёта, приходит как query string: ?tab=raports&RaportFrom=2026-06-01&RaportTo=2026-06-15
    [BindProperty(SupportsGet = true)]
    public DateTime? RaportFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? RaportTo { get; set; }

    public RaportSummaryDto RaportSummary { get; set; } = new();


public async Task LoadRaportsTabAsync(CancellationToken ct)
{
    var today = DateTime.UtcNow.Date;
    var defaultFrom = new DateTime(today.Year, today.Month, 1);
    
    // Используем значения, принудительно переведенные в .Date (00:00:00)
    var from = (RaportFrom ?? defaultFrom).Date;
    var to = (RaportTo ?? today).Date;

    if (to < from) (from, to) = (to, from);

    RaportFrom = from;
    RaportTo = to;

    // Важно: работаем в UTC, если данные в БД хранятся в UTC
    var start = from.ToUniversalTime(); 
    var end = to.AddDays(1).ToUniversalTime();

    // Диагностика: выведет в логи контейнера реальные данные
    Console.WriteLine($"[DEBUG] Поиск раппортов с {start} по {end}");

    var agg = await _db.Raports
        .AsNoTracking()
        .Where(r => r.CreatedAt >= start && r.CreatedAt < end)
        .GroupBy(_ => 1)
        .Select(g => new
        {
            Count = g.Count(),
            FactCash = g.Sum(r => (long?)r.FactCash) ?? 0,
            FactNonCash = g.Sum(r => (long?)r.FactNonCash) ?? 0,
            ProgramCash = g.Sum(r => (long?)r.ProgramCash) ?? 0,
            ProgramNonCash = g.Sum(r => (long?)r.ProgramNonCash) ?? 0,
            FactSafe = g.Sum(r => (long?)r.FactSafe) ?? 0
        })
        .FirstOrDefaultAsync(ct);

    Console.WriteLine($"[DEBUG] Найдено записей: {agg?.Count ?? 0}");

    RaportSummary = new RaportSummaryDto
    {
        Count = (int)(agg?.Count ?? 0),
        FactCash = (int)(agg?.FactCash ?? 0),
        FactNonCash = (int)(agg?.FactNonCash ?? 0),
        ProgramCash = (int)(agg?.ProgramCash ?? 0),
        ProgramNonCash = (int)(agg?.ProgramNonCash ?? 0),
        FactSafe = (int)(agg?.FactSafe ?? 0)
    };
}
    /// <summary>
    /// Сводка по раппортам за период. Вычисляемые суммы — в самом DTO,
    /// чтобы не дублировать арифметику в Razor.
    /// </summary>
    public class RaportSummaryDto
    {
        public int Count { get; set; }
        public int FactCash { get; set; }
        public int FactNonCash { get; set; }
        public int ProgramCash { get; set; }
        public int ProgramNonCash { get; set; }
        public int FactSafe { get; set; }

        public int FactTotal => FactCash + FactNonCash;
        public int ProgramTotal => ProgramCash + ProgramNonCash;

        public int DiffCash => FactCash - ProgramCash;
        public int DiffNonCash => FactNonCash - ProgramNonCash;
        public int DiffTotal => FactTotal - ProgramTotal;
    }
}