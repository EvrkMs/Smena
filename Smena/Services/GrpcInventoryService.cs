using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Inventory;
using Host.Services.Operations;
using Host.Services.Telegram;

namespace Host.Services;

public class GrpcInventoryService(
    InventoryOperationsService inventoryService,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Inventory.GrpcInventoryService.GrpcInventoryServiceBase
{
    private readonly InventoryOperationsService _inventoryService = inventoryService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    public override async Task<BoolResponse> SendInventory(GrpcInventoryRequest request, ServerCallContext context)
    {
        var employeeIds = request.EmployeeIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var scope = _scopeAccessor.Current
            ?? throw new InvalidOperationException("Telegram scope is not available.");

        var result = await _inventoryService.SendInventoryAsync(
            request.TotalAmount,
            employeeIds,
            request.Comment,
            scope,
            context.CancellationToken);

        return new BoolResponse { Success = result.Success, Message = result.Message };
    }
}
