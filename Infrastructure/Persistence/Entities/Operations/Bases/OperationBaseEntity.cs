using Infrastructure.Persistence.Entities.Bases;
using System.Numerics;

namespace Infrastructure.Persistence.Entities.Operations.Bases;

public class OperationBaseEntity<TType, TAmount> : Entity<Guid>
    where TType : Enum
    where TAmount : INumber<TAmount>
{
    public TAmount Amount { get; set; }

    public TType Type { get; set; }

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}