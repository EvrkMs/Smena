using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Raport;
using Host.Services.Operations;
using Host.Services.Telegram;

namespace Host.Services;

public class GrpcRaportService(
    RaportOperationsService raportService,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Raport.GrpcRaportService.GrpcRaportServiceBase
{
    private readonly RaportOperationsService _raportService = raportService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    public override async Task<BoolResponse> SendRaport(GrpcRaportRequest request, ServerCallContext context)
    {
        var employees = request.Employees
            .Select(e => new RaportOperationsService.RaportEmployeeInput(
                Guid.TryParse(e.EmployeeId, out var id) ? id : Guid.Empty,
                e.Hours,
                e.Minus))
            .ToList();

        var input = new RaportOperationsService.RaportInput(
            request.FactCash,
            request.FactNonCash,
            request.ProgramCash,
            request.ProgramNonCash,
            request.FactSafe,
            request.WhyMinus,
            request.SendPhoto,
            request.PhotoSessionKey,
            employees);

        var scope = _scopeAccessor.Current
            ?? throw new InvalidOperationException("Telegram scope is not available.");

        var result = await _raportService.SendRaportAsync(input, scope, context.CancellationToken);

        return new BoolResponse { Success = result.Success, Message = result.Message };
    }
}
