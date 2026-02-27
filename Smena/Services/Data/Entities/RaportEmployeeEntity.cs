namespace Host.Services.Data.Entities;

public class RaportEmployeeEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RaportId { get; set; }
    public Guid EmployeeId { get; set; }
    public int Hours { get; set; }
    public int Minus { get; set; }
    public int Salary { get; set; }

    public RaportEntity Raport { get; set; } = null!;
}
