using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Employee;
using Host.Services.Operations;

namespace Host.Services;

public class GrpcEmployeeService(EmployeeOperationsService employeeService)
    : Host.Grpc.Services.Employee.GrpcEmployeeService.GrpcEmployeeServiceBase
{
    private readonly EmployeeOperationsService _employeeService = employeeService;

    public override async Task<GrpcEmployeesResponse> EmployeesList(Empty request, ServerCallContext context)
    {
        var employees = await _employeeService.ListAsync(context.CancellationToken);

        var response = new GrpcEmployeesResponse();
        response.Employees.AddRange(employees.Select(e => new GrpcEmployee
        {
            Id = e.Id.ToString(),
            Name = e.Name,
            HourlyRate = e.HourlyRate,
            SalaryThreadId = e.SalaryThreadId,
            TelegramId = e.TelegramId
        }));

        return response;
    }

    public override async Task<BoolResponse> EmployeeAdd(GrpcEmployee request, ServerCallContext context)
    {
        var id = Guid.TryParse(request.Id, out var parsed) ? parsed : (Guid?)null;

        var result = await _employeeService.AddAsync(
            id,
            request.Name,
            request.HourlyRate,
            request.SalaryThreadId,
            request.TelegramId,
            context.CancellationToken);

        return new BoolResponse { Success = result.Success, Message = result.Message };
    }

    public override async Task<GrpcEmployeeSalaryResponse> EmployeeCurrentSalary(
        GrpcEmployeeSalaryRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmployeeId, out var employeeId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid employee_id."));
        }

        try
        {
            var currentSalary = await _employeeService.GetCurrentSalaryAsync(
                employeeId, context.CancellationToken);

            return new GrpcEmployeeSalaryResponse { CurrentSalary = currentSalary };
        }
        catch (KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Employee not found."));
        }
    }
}
