using Infrastructure.Persistence.Entities.Operations.Bases;

namespace Infrastructure.Persistence.Entities.Operations;

public class SafeOperationEntity : OperationBaseEntity<SafeOperationTypeEntity, int>;
public enum SafeOperationTypeEntity
{
    Coming = 0,
    Exponse = 1,
}
