namespace Host.Services.Data.Entities;

public class EmployeeEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int HourlyRate { get; set; }
    public int SalaryThreadId { get; set; }

    public TelegramAccountEntity? TelegramAccount { get; set; }
    public ICollection<SalaryOperationEntity> SalaryOperations { get; set; } = [];
}
