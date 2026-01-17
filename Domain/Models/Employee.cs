namespace Domain.Models;

public class Employee
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public required string Name
    {
        get => field;
        set => field = value.Trim();
    }
}
