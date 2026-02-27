namespace Host.Services.Data.Entities;

public class TelegramAccountEntity
{
    public Guid EmployeeId { get; set; }
    public long TelegramId { get; set; }

    public EmployeeEntity Employee { get; set; } = null!;
}
