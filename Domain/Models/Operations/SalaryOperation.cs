using Domain.Common;
using Domain.Models.Operations.Base;

namespace Domain.Models.Operations;

public class SalaryOperation : OperationBase<SalaryOperationType, int>
{
    public override int SignedAmount()
    {
        return Type switch
        {
            SalaryOperationType.Regular | SalaryOperationType.Bonus => +Amount,

            SalaryOperationType.Advance |
            SalaryOperationType.Inventory |
            SalaryOperationType.Fine => -Amount,

            _ => throw new DomainException("Not found operation Type."),
        };
    }

    public required Guid EmployeeId {
        get => field;
        set
        {
            if (value == Guid.Empty)
                throw new DomainException("SalaryOperation must belong to an employee.");

            field = value;
        }
    }
}

public enum SalaryOperationType
{
    Regular,
    Bonus,

    Advance,
    Inventory,
    Fine
}
