namespace Host.Services.Data.Entities;

public class ExpenseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Amount { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool FromSafe { get; set; }
    public bool IsNonCash { get; set; }
    public bool SendPhoto { get; set; }
    public string? PhotoSessionKey { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
