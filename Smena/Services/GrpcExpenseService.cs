using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Expense;
using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Host.Services.Photo;
using Host.Services.Telegram;
using Microsoft.Extensions.Options;

namespace Host.Services;

public class GrpcExpenseService(
    AppDbContext db,
    TelegramService telegramService,
    PhotoSessionStore photoSessionStore,
    IOptions<PhotoOptions> photoOptions,
    SafeOperationsService safeOperationsService,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Expense.GrpcExpense.GrpcExpenseBase
{
    private readonly AppDbContext _db = db;
    private readonly TelegramService _telegramService = telegramService;
    private readonly PhotoSessionStore _photoSessionStore = photoSessionStore;
    private readonly PhotoOptions _photoOptions = photoOptions.Value;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    public override async Task<BoolResponse> AddExpenseOperation(GrpcExpenseAdd request, ServerCallContext context)
    {
        if (request.Amount <= 0)
        {
            return new BoolResponse { Success = false, Message = "Invalid amount." };
        }

        var scope = _scopeAccessor.Current ?? throw new InvalidOperationException("Telegram scope is not available.");

        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            bool fromSafe = request.FromSafe && !request.IsNonCash;

            var expense = new ExpenseEntity
            {
                Amount = request.Amount,
                Comment = request.Comment ?? string.Empty,
                FromSafe = fromSafe,
                IsNonCash = request.IsNonCash,
                SendPhoto = request.SendPhoto,
                PhotoSessionKey = string.IsNullOrWhiteSpace(request.PhotoSessionKey) ? null : request.PhotoSessionKey,
                SenderName = request.SenderName ?? string.Empty
            };

            _db.Expenses.Add(expense);

            if (fromSafe)
            {
                await _safeOperationsService.ApplySafeOperationAsync(
                    -request.Amount,
                    $"Расход: {request.Comment}",
                    scope,
                    context.CancellationToken);
            }
            else if (request.IsNonCash)
            {
                var nonCashComment = string.IsNullOrWhiteSpace(request.Comment)
                    ? "Расход (Б/Н)"
                    : $"Расход (Б/Н): {request.Comment}";

                _db.NonCashOperations.Add(new NonCashOperationEntity
                {
                    Amount = request.Amount,
                    Comment = nonCashComment,
                    Type = NonCashOperationType.Expense
                });
            }

            if (request.SendPhoto)
            {
                if (string.IsNullOrWhiteSpace(request.PhotoSessionKey))
                {
                    throw new InvalidOperationException("Photo session key is required.");
                }

                if (!_photoSessionStore.TryGetSession(
                        request.PhotoSessionKey,
                        TimeSpan.FromSeconds(_photoOptions.SessionTtlSeconds),
                        out var fileIds))
                {
                    throw new InvalidOperationException("Photos not found or session expired.");
                }

                await _telegramService.SendExpensePhotosAsync(fileIds, scope, context.CancellationToken);
            }

            await _telegramService.SendExpenseAsync(expense, scope, context.CancellationToken);

            await _db.SaveChangesAsync(context.CancellationToken);
        }, context.CancellationToken);

        if (!string.IsNullOrWhiteSpace(request.PhotoSessionKey))
        {
            _photoSessionStore.RemoveSession(request.PhotoSessionKey);
        }

        return new BoolResponse { Success = true, Message = "Расход добавлен." };
    }

}


