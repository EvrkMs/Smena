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
        // Битые id отклоняем целиком: раньше они молча отбрасывались, и сумма
        // делилась между «выжившими» сотрудниками с ответом об успехе.
        var employeeIds = new List<Guid>();
        foreach (var raw in request.EmployeeIds)
        {
            if (!Guid.TryParse(raw, out var guid) || guid == Guid.Empty)
            {
                return new BoolResponse { Success = false, Message = $"Некорректный employee_id: \"{raw}\"." };
            }

            employeeIds.Add(guid);
        }

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
