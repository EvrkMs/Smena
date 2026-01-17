using Application.Interface;
using Domain.Common;
using Domain.Models.Operations;
using Infrastructure.Persistence.Contexts;
using Infrastructure.Persistence.Entities.Operations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

internal class SalaryOperationRepository(AppDbContext db) : ISalaryOperationRepository, IOperationRepository<SalaryOperationType, int, SalaryOperation>
{
    private readonly AppDbContext _db = db;

    public async Task<Result<SalaryOperation>> AddAsync(SalaryOperation operation)
    {
        var entity = new SalaryOperationEntity
        {
            Amount = operation.Amount,
            Comment = operation.Comment,
            EmployeeId = operation.EmployeeId,
            Type = MapDomainToEntity(operation.Type),
        };

        var addEntity = await _db.SalaryOperations.AddAsync(entity);

        return Result<SalaryOperation>.Ok(new()
        {
            Amount = addEntity.Entity.Amount,
            Comment = addEntity.Entity.Comment,
            EmployeeId = addEntity.Entity.EmployeeId,
            Type = MapEntityToDomain(addEntity.Entity.Type)
        });
    }
    public async Task<int> GetSalaryByEmployeeId(Guid id)
    {
        return await _db.SalaryOperations
            .AsNoTracking()
            .Where(o => o.EmployeeId == id)
            .SumAsync(x => x.Amount);
    }
    public static SalaryOperationTypeEntity MapDomainToEntity(SalaryOperationType type)
    => type switch
    {
        SalaryOperationType.Regular => SalaryOperationTypeEntity.Regular,
        SalaryOperationType.Bonus => SalaryOperationTypeEntity.Bonus,
        SalaryOperationType.Advance => SalaryOperationTypeEntity.Advance,
        SalaryOperationType.Inventory => SalaryOperationTypeEntity.Inventory,
        SalaryOperationType.Fine => SalaryOperationTypeEntity.Fine,
        _ => throw new InvalidOperationException("Not found this SalaryOperation type")
    };

    public static SalaryOperationType MapEntityToDomain(SalaryOperationTypeEntity type)
        => type switch
        {
            SalaryOperationTypeEntity.Regular => SalaryOperationType.Regular,
            SalaryOperationTypeEntity.Bonus => SalaryOperationType.Bonus,
            SalaryOperationTypeEntity.Advance => SalaryOperationType.Advance,
            SalaryOperationTypeEntity.Inventory => SalaryOperationType.Inventory,
            SalaryOperationTypeEntity.Fine => SalaryOperationType.Fine,
            _ => throw new InvalidOperationException("Not found this SalaryOperationEntity type")
        };
}