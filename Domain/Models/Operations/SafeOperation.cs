using Domain.Common;
using Domain.Events.Interface;
using Domain.Models.Operations.Base;

namespace Domain.Models.Operations;

public record SafeOperationCreated(
    int Amount,
    SafeOperationType Type,
    DateTime OccurredOn
) : IDomainEvent;

public class SafeOperation : OperationBase<SafeOperationType, int>
{
    public SafeOperation(int amount, SafeOperationType type)
    {
        this.Amount = amount;
        this.Type = type;

        AddDomainEvent(new SafeOperationCreated(Amount, Type, DateTime.UtcNow));
    }
    public override int SignedAmount()
    {
        return Type switch
        {
            (SafeOperationType.Coming) => +Amount,

            (SafeOperationType.Exponse) => -Amount,

            _ => throw new DomainException("Not fount operation Type"),
        };
    }

    private void AddDomainEvent(IDomainEvent domainEvent)
    {
        // _events хранится в базовом классе
        (_events as List<IDomainEvent>).Add(domainEvent);
    }
}
public enum SafeOperationType
{
    Coming,
    Exponse,
}
