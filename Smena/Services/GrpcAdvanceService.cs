using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Advance;
using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Host.Services.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Host.Services;

public class GrpcAdvanceService(
    AppDbContext db,
    SalaryOperationsService salaryOperationsService,
    SafeOperationsService safeOperationsService,
    SafeUpdatesNotifier safeUpdatesNotifier,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Advance.GrpcAdvanceService.GrpcAdvanceServiceBase
{
    private readonly AppDbContext _db = db;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly SafeUpdatesNotifier _safeUpdatesNotifier = safeUpdatesNotifier;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

    public override async Task<BoolResponse> SendAdvance(GrpcAdvanceRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmployeeId, out var employeeId))
        {
            return new BoolResponse { Success = false, Message = "Invalid employee_id." };
        }

        if (request.Amount <= 0)
        {
            return new BoolResponse { Success = false, Message = "Invalid amount." };
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, context.CancellationToken);

        if (employee == null)
        {
            return new BoolResponse { Success = false, Message = "Employee not found." };
        }

        var currentSalary = await _salaryOperationsService.GetCurrentSalaryAsync(
            employeeId,
            context.CancellationToken);

        if (currentSalary <= 0)
        {
            return new BoolResponse { Success = false, Message = "У сотрудника нет доступной ЗП для выплаты." };
        }

        if (request.Amount > currentSalary)
        {
            return new BoolResponse
            {
                Success = false,
                Message = $"Нельзя выдать больше текущей ЗП ({currentSalary} руб.)."
            };
        }

        var type = request.IsSalary ? SalaryOperationType.Pay : SalaryOperationType.Advance;
        string comment = string.IsNullOrWhiteSpace(request.Comment)
            ? (request.IsSalary ? "ЗП" : "Аванс")
            : request.Comment;

        bool extractFromSafe = request.ExtractFromSafe && !request.IsNonCash;

        var scope = _scopeAccessor.Current ?? throw new InvalidOperationException("Telegram scope is not available.");
        int? updatedSafe = null;

        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            var signedDelta = SignedSalaryAmount(type, request.Amount);
            await _salaryOperationsService.ApplySalaryOperationAsync(
                employeeId,
                signedDelta,
                type,
                comment,
                scope,
                context.CancellationToken);

            if (extractFromSafe)
            {
                updatedSafe = await _safeOperationsService.ApplySafeOperationAsync(
                    -request.Amount,
                    $"{employee.Name}: {comment}",
                    scope,
                    context.CancellationToken);
            }
            else if (request.IsNonCash)
            {
                _db.NonCashOperations.Add(new NonCashOperationEntity
                {
                    Amount = request.Amount,
                    Comment = $"{employee.Name}: {comment}",
                    Type = NonCashOperationType.Expense
                });
            }

            await _db.SaveChangesAsync(context.CancellationToken);
        }, context.CancellationToken);

        if (updatedSafe.HasValue)
        {
            _safeUpdatesNotifier.Publish(updatedSafe.Value);
        }

        return new BoolResponse { Success = true, Message = "Operation completed." };
    }

    private static int SignedSalaryAmount(SalaryOperationType type, int amount)
        => type switch
        {
            SalaryOperationType.Regular or SalaryOperationType.Bonus => amount,
            _ => -amount
        };
}
