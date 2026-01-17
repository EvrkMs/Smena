using Infrastructure.Persistence.Entities;
using Infrastructure.Persistence.Entities.Operations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Contexts;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<EmployeeEntity> Employees { get; set; }
    public DbSet<SalaryOperationEntity> SalaryOperations { get; set; }
    public DbSet<SafeOperationEntity> SafeOperations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
