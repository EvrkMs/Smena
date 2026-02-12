namespace Host.Services.Data.Entities;

public class NonCashOperationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Amount { get; set; }
    public NonCashOperationType Type { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum NonCashOperationType
{
    Coming = 1,
    Expense = 2
}
