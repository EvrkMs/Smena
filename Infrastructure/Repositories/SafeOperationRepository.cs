using Application.Interface;
using Domain.Common;
using Domain.Models.Operations;
using Infrastructure.Persistence.Contexts;
using Infrastructure.Persistence.Entities.Operations;

namespace Infrastructure.Repositories;

internal class SafeOperationRepository(AppDbContext db) : ISafeOperationRepository, IOperationRepository<SafeOperationType, int, SafeOperation>
{
    private readonly AppDbContext _db = db;
    public async Task<Result<SafeOperation>> AddAsync(SafeOperation operation)
    {
        try
        {
            var entity = new SafeOperationEntity
            {
                Amount = operation.Amount,
                Comment = operation.Comment,
                Type = MapDomainToEntity(operation.Type)
            };

            var addEntity = await _db.SafeOperations.AddAsync(entity);

            return Result<SafeOperation>.Ok(new SafeOperation
            {
                Amount = addEntity.Entity.Amount,
                Comment = addEntity.Entity.Comment,
                Type = MapEntityToDomain(addEntity.Entity.Type)
            });
        }
        catch (DomainException ex)
        {
            return Result<SafeOperation>.Fail("Error in operation add SafeOperation:\n" + ex.Message);
        }
    }

    public static SafeOperationTypeEntity MapDomainToEntity(SafeOperationType type)
        => type switch
        {
            (SafeOperationType.Coming) => SafeOperationTypeEntity.Coming,

            (SafeOperationType.Exponse) => SafeOperationTypeEntity.Exponse,

            _ => throw new InvalidOperationException("Not fount this Operation type"),
        };

    public static SafeOperationType MapEntityToDomain(SafeOperationTypeEntity type)
        => type switch
        {
            (SafeOperationTypeEntity.Coming) => SafeOperationType.Coming,

            (SafeOperationTypeEntity.Exponse) => SafeOperationType.Exponse,

            _ => throw new InvalidOperationException("Not fount this Operation type"),
        };
}
