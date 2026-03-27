using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Sockets;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Host.Services.Telegram;

public class TelegramService(
    IOptions<TelegramOptions> options,
    AppDbContext db,
    ILogger<TelegramMessageScope> scopeLogger,
    ILogger<TelegramService> logger)
{
    private const int RetryAttempts = 4;

    private readonly TelegramOptions _options = options.Value;
    private readonly AppDbContext _db = db;
    private readonly ILogger<TelegramMessageScope> _scopeLogger = scopeLogger;
    private readonly ILogger<TelegramService> _logger = logger;
    private static ITelegramBotClient? s_sharedBotClient;
    private static readonly object s_clientLock = new();

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
        string senderPart = string.IsNullOrWhiteSpace(expense.SenderName)
            ? string.Empty
            : $" ({expense.SenderName})";

        string msg = $"{BusinessNow():yyyy.MM.dd HH:mm}\n{expense.Amount} {expense.Comment}{senderPart}";

        var message = await ExecuteWithRetryAsync(
            (bot, token) => bot.SendMessage(
                _options.ForwardChatId,
                msg,
                messageThreadId: _options.ExpensesThreadId,
                cancellationToken: token),
            ct);

        scope.Track(message.Chat.Id, message.MessageId);
    }

    public async Task SendExpensePhotosAsync(
        IReadOnlyList<string> fileIds,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (fileIds.Count == 0)
        {
            return;
        }

        var media = fileIds
            .Select(id => new InputMediaPhoto(new InputFileId(id)))
            .Cast<IAlbumInputMedia>()
            .ToList();

        var messages = await ExecuteWithRetryAsync(
            (bot, token) => bot.SendMediaGroup(
                _options.ForwardChatId,
                media,
                messageThreadId: _options.ExpensesThreadId,
                cancellationToken: token),
            ct);

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
        string sign = amount >= 0 ? "+" : string.Empty;
        string msg = $"{BusinessNow():yyyy.MM.dd HH:mm}\n{sign}{amount} {comment}\nТеперь сейф: {currentSafe}";
        int? threadId = _options.SafeThreadId > 0 ? _options.SafeThreadId : null;

        var message = await ExecuteWithRetryAsync(
            (bot, token) => bot.SendMessage(
                _options.ForwardChatId,
                msg,
                messageThreadId: threadId,
                cancellationToken: token),
            ct);

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
        string msg = $"{BusinessNow():yyyy.MM.dd HH:mm}\n{sign}{amount} {employee.Name} {entryLabel}\nТеперь: {currentSalary}";

        int? threadId = employee.SalaryThreadId > 0 ? employee.SalaryThreadId : null;
        var message = await ExecuteWithRetryAsync(
            (bot, token) => bot.SendMessage(
                _options.SalaryChatId,
                msg,
                messageThreadId: threadId,
                cancellationToken: token),
            ct);

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

        var message = await ExecuteWithRetryAsync(
            (bot, token) => bot.SendMessage(
                _options.ForwardChatId,
                sb.ToString(),
                messageThreadId: _options.RaportThreadId,
                cancellationToken: token),
            ct);

        scope.Track(message.Chat.Id, message.MessageId);
    }

    public async Task SendRaportPhotosAsync(
        IReadOnlyList<string> fileIds,
        TelegramMessageScope scope,
        CancellationToken ct)
    {
        if (fileIds.Count == 0)
        {
            return;
        }

        var media = fileIds
            .Select(id => new InputMediaPhoto(new InputFileId(id)))
            .Cast<IAlbumInputMedia>()
            .ToList();

        var messages = await ExecuteWithRetryAsync(
            (bot, token) => bot.SendMediaGroup(
                _options.ForwardChatId,
                media,
                messageThreadId: _options.RaportThreadId,
                cancellationToken: token),
            ct);

        foreach (var message in messages)
        {
            scope.Track(message.Chat.Id, message.MessageId);
        }
    }

    public ITelegramBotClient GetClientOrThrow()
    {
        if (s_sharedBotClient != null)
            return s_sharedBotClient;

        lock (s_clientLock)
        {
            if (s_sharedBotClient != null)
                return s_sharedBotClient;

            if (string.IsNullOrWhiteSpace(_options.Token))
                throw new InvalidOperationException("Telegram token is not configured.");

            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15)
            };

            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds)
            };

            s_sharedBotClient = new TelegramBotClient(_options.Token, httpClient);
            return s_sharedBotClient;
        }
    }

    private static DateTimeOffset BusinessNow()
    {
        // Moscow time (UTC+3) — matches business timezone for Telegram messages.
        return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3));
    }

    public Task<T> ExecuteWithRetryAsync<T>(
        Func<ITelegramBotClient, CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        return ExecuteWithRetryCoreAsync(action, ct);
    }

    private async Task<T> ExecuteWithRetryCoreAsync<T>(
        Func<ITelegramBotClient, CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        var client = GetClientOrThrow();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= RetryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await action(client, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < RetryAttempts)
            {
                lastError = ex;
                var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Transient Telegram error on attempt {Attempt}/{MaxAttempts}. Retrying after {DelayMs} ms.",
                    attempt,
                    RetryAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw lastError ?? new InvalidOperationException("Telegram operation failed without an exception.");
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (ex is RequestException requestEx)
        {
            return requestEx.InnerException == null || IsTransient(requestEx.InnerException);
        }

        if (ex is HttpRequestException httpEx)
        {
            return httpEx.InnerException == null || IsTransient(httpEx.InnerException);
        }

        if (ex is SocketException socketEx)
        {
            return socketEx.SocketErrorCode is SocketError.TryAgain
                or SocketError.NetworkUnreachable
                or SocketError.TimedOut
                or SocketError.HostUnreachable
                or SocketError.ConnectionReset
                or SocketError.ConnectionAborted;
        }

        if (ex is TaskCanceledException or TimeoutException)
        {
            return true;
        }

        return false;
    }
}
