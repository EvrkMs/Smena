using Infrastructure.Persistence.Entities.Bases;
using Infrastructure.Persistence.Entities.Operations;

namespace Infrastructure.Persistence.Entities;

public class EmployeeEntity : Entity<Guid>
{
    public string Name { get; set; } = null!;

    public ICollection<SalaryOperationEntity> SalaryOperationEntities { get; set; }
}
