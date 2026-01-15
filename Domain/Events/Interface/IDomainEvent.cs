namespace Domain.Events.Interface;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}
