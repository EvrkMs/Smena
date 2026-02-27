using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Employee;
using Host.Services.Data;
using Host.Services.Data.Entities;
using Host.Services.Operations;
using Microsoft.EntityFrameworkCore;

namespace Host.Services;

public class GrpcEmployeeService(AppDbContext db, SalaryOperationsService salaryOperationsService)
    : Host.Grpc.Services.Employee.GrpcEmployeeService.GrpcEmployeeServiceBase
{
    private readonly AppDbContext _db = db;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;

    public override async Task<GrpcEmployeesResponse> EmployeesList(Empty request, ServerCallContext context)
    {
        var employees = await _db.Employees
            .Include(e => e.TelegramAccount)
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .ToListAsync(context.CancellationToken);

        var response = new GrpcEmployeesResponse();
        response.Employees.AddRange(employees.Select(MapToGrpc));

        return response;
    }

    public override async Task<BoolResponse> EmployeeAdd(GrpcEmployee request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new BoolResponse { Success = false, Message = "Employee name is required." };
        }

        if (request.HourlyRate < 0)
        {
            return new BoolResponse { Success = false, Message = "Invalid hourly rate." };
        }

        var employee = new EmployeeEntity
        {
            Id = Guid.TryParse(request.Id, out var id) ? id : Guid.NewGuid(),
            Name = request.Name.Trim(),
            HourlyRate = request.HourlyRate,
            SalaryThreadId = request.SalaryThreadId
        };

        _db.Employees.Add(employee);

        if (request.TelegramId != 0)
        {
            _db.TelegramAccounts.Add(new TelegramAccountEntity
            {
                EmployeeId = employee.Id,
                TelegramId = request.TelegramId
            });
        }

        await _db.SaveChangesAsync(context.CancellationToken);

        return new BoolResponse { Success = true, Message = "Employee added." };
    }

    public override async Task<GrpcEmployeeSalaryResponse> EmployeeCurrentSalary(
        GrpcEmployeeSalaryRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmployeeId, out var employeeId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid employee_id."));
        }

        var exists = await _db.Employees
            .AsNoTracking()
            .AnyAsync(e => e.Id == employeeId, context.CancellationToken);

        if (!exists)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Employee not found."));
        }

        var currentSalary = await _salaryOperationsService.GetCurrentSalaryAsync(
            employeeId,
            context.CancellationToken);

        return new GrpcEmployeeSalaryResponse { CurrentSalary = currentSalary };
    }

    private static GrpcEmployee MapToGrpc(EmployeeEntity entity)
    {
        return new GrpcEmployee
        {
            Id = entity.Id.ToString(),
            Name = entity.Name,
            HourlyRate = entity.HourlyRate,
            SalaryThreadId = entity.SalaryThreadId,
            TelegramId = entity.TelegramAccount?.TelegramId ?? 0
        };
    }
}
