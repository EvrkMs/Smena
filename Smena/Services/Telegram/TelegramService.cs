using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Host.Services.Telegram;

public class TelegramService(
    IOptions<TelegramOptions> options,
    AppDbContext db,
    ILogger<TelegramMessageScope> scopeLogger)
{
    private readonly TelegramOptions _options = options.Value;
    private readonly AppDbContext _db = db;
    private readonly ILogger<TelegramMessageScope> _scopeLogger = scopeLogger;
    private ITelegramBotClient? _botClient;

    public TelegramMessageScope CreateScope() => new(GetClientOrThrow(), _scopeLogger);

    public sealed record RaportEmployeeSummary(
        string Name,
        int Hours,
        int Minus,
        int Salary);

    public async Task SendExpenseAsync(
        ExpenseEntity expense,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var client = GetClientOrThrow();
        string senderPart = string.IsNullOrWhiteSpace(expense.SenderName)
            ? string.Empty
            : $" ({expense.SenderName})";

        string msg = $"{DateTime.Now:yyyy.MM.dd HH:mm}\n{expense.Amount} {expense.Comment}{senderPart}";

        var message = await client.SendMessage(
            _options.ForwardChatId,
            msg,
            messageThreadId: _options.ExpensesThreadId,
            cancellationToken: ct);

        scope.Track(message.Chat.Id, message.MessageId);
    }

    public async Task SendExpensePhotosAsync(
        IReadOnlyList<string> fileIds,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var client = GetClientOrThrow();
        if (fileIds.Count == 0)
        {
            return;
        }

        var media = fileIds
            .Select(id => new InputMediaPhoto(new InputFileId(id)))
            .Cast<IAlbumInputMedia>()
            .ToList();

        var messages = await client.SendMediaGroup(
            _options.ForwardChatId,
            media,
            messageThreadId: _options.ExpensesThreadId,
            cancellationToken: ct);

        foreach (var message in messages)
        {
            scope.Track(message.Chat.Id, message.MessageId);
        }
    }

    public async Task SendSafeAsync(
        int amount,
        string comment,
        int currentSafe,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var client = GetClientOrThrow();
        string sign = amount >= 0 ? "+" : string.Empty;
        string msg = $"{DateTime.Now:yyyy.MM.dd HH:mm}\n{sign}{amount} {comment}\nТеперь сейф: {currentSafe}";
        int? threadId = _options.SafeThreadId > 0 ? _options.SafeThreadId : null;

        var message = await client.SendMessage(
            _options.ForwardChatId,
            msg,
            messageThreadId: threadId,
            cancellationToken: ct);

        scope.Track(message.Chat.Id, message.MessageId);
    }

    public async Task SendSalaryAsync(
        Guid employeeId,
        int amount,
        SalaryOperationType type,
        int currentSalary,
        string comment,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var client = GetClientOrThrow();
        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new InvalidOperationException("Employee not found for salary message.");

        string action = type switch
        {
            SalaryOperationType.Advance => "Аванс",
            SalaryOperationType.Pay => "ЗП",
            SalaryOperationType.Inventory => "Инвентаризация",
            SalaryOperationType.Fine => "Штраф",
            SalaryOperationType.Bonus => "Бонус",
            _ => "Смена"
        };

        string entryLabel = string.IsNullOrWhiteSpace(comment) ? action : comment;

        string sign = amount >= 0 ? "+" : string.Empty;
        string msg = $"{DateTime.Now:yyyy.MM.dd HH:mm}\n{sign}{amount} {employee.Name} {entryLabel}\nТеперь: {currentSalary}";

        int? threadId = employee.SalaryThreadId > 0 ? employee.SalaryThreadId : null;
        var message = await client.SendMessage(
            _options.SalaryChatId,
            msg,
            messageThreadId: threadId,
            cancellationToken: ct);

        scope.Track(message.Chat.Id, message.MessageId);
    }

    public async Task SendRaportAsync(
        RaportEntity raport,
        IReadOnlyList<RaportEmployeeSummary> employees,
        int programSafe,
        int revenue,
        int totalSalary,
        int total,
        int cashDiscrepancy,
        int safeDiscrepancy,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var client = GetClientOrThrow();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(raport.CreatedAt.ToString("dd.MM.yyyy HH:mm"));
        sb.AppendLine();
        sb.AppendLine($"Нал: {raport.FactCash}");
        sb.AppendLine($"Б/Н: {raport.FactNonCash}");
        sb.AppendLine($"Выручка: {revenue}");
        sb.AppendLine($"Итог: {total}");
        sb.AppendLine();
        sb.AppendLine($"Минус по кассе: {cashDiscrepancy}");
        sb.AppendLine($"Минус по сейфу: {safeDiscrepancy}");
        sb.AppendLine();
        sb.AppendLine($"Факт сейфа: {raport.FactSafe}");
        sb.AppendLine();
        sb.AppendLine("==програмные данные==");
        sb.AppendLine($"Нал: {raport.ProgramCash}");
        sb.AppendLine($"Безнал: {raport.ProgramNonCash}");
        sb.AppendLine($"Сейф: {programSafe}");

        if (!string.IsNullOrWhiteSpace(raport.WhyMinus))
        {
            sb.AppendLine();
            sb.AppendLine($"Причина минуса: {raport.WhyMinus}");
        }

        if (employees.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Сотрудники:");
            foreach (var emp in employees)
            {
                sb.AppendLine($"- {emp.Name}: {emp.Hours}ч, минус {emp.Minus}, ЗП {emp.Salary}");
            }
        }

        if (totalSalary > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Всего ЗП: {totalSalary} руб.");
        }

        var message = await client.SendMessage(
            _options.ForwardChatId,
            sb.ToString(),
            messageThreadId: _options.RaportThreadId,
            cancellationToken: ct);

        scope.Track(message.Chat.Id, message.MessageId);
    }

    public async Task SendRaportPhotosAsync(
        IReadOnlyList<string> fileIds,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        var client = GetClientOrThrow();
        if (fileIds.Count == 0)
        {
            return;
        }

        var media = fileIds
            .Select(id => new InputMediaPhoto(new InputFileId(id)))
            .Cast<IAlbumInputMedia>()
            .ToList();

        var messages = await client.SendMediaGroup(
            _options.ForwardChatId,
            media,
            messageThreadId: _options.RaportThreadId,
            cancellationToken: ct);

        foreach (var message in messages)
        {
            scope.Track(message.Chat.Id, message.MessageId);
        }
    }

    public ITelegramBotClient GetClientOrThrow()
    {
        if (_botClient != null)
        {
            return _botClient;
        }

        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("Telegram token is not configured.");
        }

        _botClient = new TelegramBotClient(_options.Token);
        return _botClient;
    }
}

