using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Advance;
using Host.Services.Operations;
using Host.Services.Telegram;

namespace Host.Services;

public class GrpcAdvanceService(
    AdvanceOperationsService advanceService,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Advance.GrpcAdvanceService.GrpcAdvanceServiceBase
{
    private readonly AdvanceOperationsService _advanceService = advanceService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    public override async Task<BoolResponse> SendAdvance(GrpcAdvanceRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmployeeId, out var employeeId))
        {
            return new BoolResponse { Success = false, Message = "Invalid employee_id." };
        }

        var scope = _scopeAccessor.Current
            ?? throw new InvalidOperationException("Telegram scope is not available.");

        var result = await _advanceService.SendAdvanceAsync(
            employeeId,
            request.Amount,
            request.IsSalary,
            request.Comment,
            request.ExtractFromSafe,
            request.IsNonCash,
            scope,
            context.CancellationToken);

        return new BoolResponse { Success = result.Success, Message = result.Message };
    }
}
