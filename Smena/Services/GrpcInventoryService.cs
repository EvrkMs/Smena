using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Inventory;
using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Host.Services;

public class GrpcInventoryService(
    AppDbContext db,
    SalaryOperationsService salaryOperationsService,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Inventory.GrpcInventoryService.GrpcInventoryServiceBase
{
    private readonly AppDbContext _db = db;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    public override async Task<BoolResponse> SendInventory(GrpcInventoryRequest request, ServerCallContext context)
    {
        if (request.TotalAmount <= 0)
        {
            return new BoolResponse { Success = false, Message = "Invalid amount." };
        }

        var employeeIds = request.EmployeeIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (employeeIds.Count == 0)
        {
            return new BoolResponse { Success = false, Message = "No employees selected." };
        }

        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .AsNoTracking()
            .ToListAsync(context.CancellationToken);

        if (employees.Count == 0)
        {
            return new BoolResponse { Success = false, Message = "Employees not found." };
        }

        int perEmployee = request.TotalAmount / employees.Count;
        int remainder = request.TotalAmount % employees.Count;

        var scope = _scopeAccessor.Current ?? throw new InvalidOperationException("Telegram scope is not available.");

        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            foreach (var employee in employees.OrderBy(e => e.Name))
            {
                int amount = perEmployee + (remainder > 0 ? 1 : 0);
                if (remainder > 0)
                {
                    remainder--;
                }

                var comment = string.IsNullOrWhiteSpace(request.Comment)
                    ? "Инвентаризация"
                    : request.Comment;

                await _salaryOperationsService.ApplySalaryOperationAsync(
                    employee.Id,
                    -amount,
                    SalaryOperationType.Inventory,
                    comment,
                    scope,
                    context.CancellationToken);
            }

            await _db.SaveChangesAsync(context.CancellationToken);
        }, context.CancellationToken);

        return new BoolResponse { Success = true, Message = "Inventory processed." };
    }

}
