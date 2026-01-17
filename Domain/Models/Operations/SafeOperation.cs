using Domain.Common;
using Domain.Models.Operations.Base;

namespace Domain.Models.Operations;

public class SafeOperation : OperationBase<SafeOperationType, int>
{
    public override int SignedAmount()
    {
        return Type switch
        {
            (SafeOperationType.Coming) => +Amount,

            (SafeOperationType.Exponse) => -Amount,

            _ => throw new DomainException("Not fount operation Type"),
        };
    }
}
public enum SafeOperationType
{
    Coming,
    Exponse,
}
