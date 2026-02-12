namespace Host.Services.Data.Entities;

public class SafeOperationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Amount { get; set; }
    public SafeOperationType Type { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum SafeOperationType
{
    Coming = 1,
    Expense = 2
}
