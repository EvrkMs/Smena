namespace Host.Services.Data.Entities;

public class SalaryOperationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public int Amount { get; set; }
    public SalaryOperationType Type { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public EmployeeEntity Employee { get; set; } = null!;
}

public enum SalaryOperationType
{
    Regular = 1,
    Bonus = 2,
    Advance = 3,
    Inventory = 4,
    Fine = 5,
    Pay = 6
}
