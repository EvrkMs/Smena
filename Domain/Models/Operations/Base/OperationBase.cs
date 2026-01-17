using System.Numerics;

namespace Domain.Models.Operations.Base;

public abstract class OperationBase<TypeOperation, TAmount>
    where TypeOperation : Enum
    where TAmount : INumber<TAmount>
{

    public required TAmount Amount
    {
        get => field;
        set => field = TAmount.Abs(value);
    }
    public string Comment { get; set; } = string.Empty;
    public required TypeOperation Type { get; set; }

    public DateTime CreatedDate { get; private set; } = DateTime.UtcNow;

    public abstract int SignedAmount();
}