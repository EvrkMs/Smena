using Application.Services;
using Domain.Models.Operations;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Service.Salary;

namespace Host.Services;

public sealed class GrpcSalaryService(SalaryService salaryService) : SalaryServiceGrpc.SalaryServiceGrpcBase
{
    private readonly SalaryService _salaryService = salaryService;

    public override async Task<Empty> AddSalaryOperation(
        SalaryOperatioAdd request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmployeeId, out var employeeId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Invalid employeeId"));
        }

        var result = await _salaryService.AddOperation(new(
            Amount: request.Amount,
            Comment: request.Comment,
            EmployeeId: employeeId,
            Type: MapGrpcToDomain(request.Type)));

        if (!result.IsSuccess)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                result.Message));
        }

        return new Empty();
    }

    private static SalaryOperationType MapGrpcToDomain(SalaryOperatioTypeGrpc type)
        => type switch
        {
            SalaryOperatioTypeGrpc.Regular => SalaryOperationType.Regular,
            SalaryOperatioTypeGrpc.Bonus => SalaryOperationType.Bonus,
            SalaryOperatioTypeGrpc.Advance => SalaryOperationType.Advance,
            SalaryOperatioTypeGrpc.Inventory => SalaryOperationType.Inventory,
            SalaryOperatioTypeGrpc.Fine => SalaryOperationType.Fine,
            _ => throw new NotSupportedException(
                $"Unsupported SalaryOperatioTypeGrpc: {type}")
        };
}