using Infrastructure.Persistence.Entities.Operations.Bases;

namespace Infrastructure.Persistence.Entities.Operations;

public class SalaryOperationEntity : OperationBaseEntity<SalaryOperationTypeEntity, int>
{
    public Guid EmployeeId { get; set; }
    public EmployeeEntity EmployeeEntity { get; set; } = null!;
}
public enum SalaryOperationTypeEntity
{
    Regular = 0,
    Bonus = 1,

    Advance = 2,
    Inventory = 3,
    Fine = 4,
}
