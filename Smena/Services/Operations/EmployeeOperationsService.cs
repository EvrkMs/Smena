using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Operations;

public class EmployeeOperationsService(
    AppDbContext db,
    SalaryOperationsService salaryOperationsService)
{
    private readonly AppDbContext _db = db;
    private readonly SalaryOperationsService _salaryOperationsService = salaryOperationsService;

    public sealed record EmployeeDto(
        Guid Id,
        string Name,
        int HourlyRate,
        int SalaryThreadId,
        long TelegramId);

    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(CancellationToken ct)
    {
        var employees = await _db.Employees
            .Include(e => e.TelegramAccount)
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        return employees.Select(e => new EmployeeDto(
            e.Id,
            e.Name,
            e.HourlyRate,
            e.SalaryThreadId,
            e.TelegramAccount?.TelegramId ?? 0)).ToList();
    }

    public async Task<OperationResult> AddAsync(
        Guid? id,
        string name,
        int hourlyRate,
        int salaryThreadId,
        long telegramId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("Employee name is required.");
        }

        if (hourlyRate < 0)
        {
            return OperationResult.Fail("Invalid hourly rate.");
        }

        var employee = new EmployeeEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name.Trim(),
            HourlyRate = hourlyRate,
            SalaryThreadId = salaryThreadId
        };

        _db.Employees.Add(employee);

        if (telegramId != 0)
        {
            _db.TelegramAccounts.Add(new TelegramAccountEntity
            {
                EmployeeId = employee.Id,
                TelegramId = telegramId
            });
        }

        await _db.SaveChangesAsync(ct);

        return OperationResult.Ok("Employee added.");
    }

    public async Task<int> GetCurrentSalaryAsync(Guid employeeId, CancellationToken ct)
    {
        var exists = await _db.Employees
            .AsNoTracking()
            .AnyAsync(e => e.Id == employeeId, ct);

        if (!exists)
        {
            throw new KeyNotFoundException("Employee not found.");
        }

        return await _salaryOperationsService.GetCurrentSalaryAsync(employeeId, ct);
    }
}
