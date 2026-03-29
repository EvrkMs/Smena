using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Expense;
using Host.Services.Operations;
using Host.Services.Telegram;

namespace Host.Services;

public class GrpcExpenseService(
    ExpenseOperationsService expenseService,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Expense.GrpcExpense.GrpcExpenseBase
{
    private readonly ExpenseOperationsService _expenseService = expenseService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    public override async Task<BoolResponse> AddExpenseOperation(GrpcExpenseAdd request, ServerCallContext context)
    {
        var scope = _scopeAccessor.Current
            ?? throw new InvalidOperationException("Telegram scope is not available.");

        var result = await _expenseService.AddExpenseAsync(
            request.Amount,
            request.Comment,
            request.FromSafe,
            request.IsNonCash,
            request.SendPhoto,
            request.PhotoSessionKey,
            request.SenderName,
            scope,
            context.CancellationToken);

        return new BoolResponse { Success = result.Success, Message = result.Message };
    }
}


