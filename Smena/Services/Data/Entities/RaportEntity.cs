namespace Host.Services.Data.Entities;

public class RaportEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int FactCash { get; set; }
    public int FactNonCash { get; set; }
    public int ProgramCash { get; set; }
    public int ProgramNonCash { get; set; }
    public int FactSafe { get; set; }
    public string WhyMinus { get; set; } = string.Empty;
    public string? PhotoSessionKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RaportEmployeeEntity> Employees { get; set; } = [];
}
