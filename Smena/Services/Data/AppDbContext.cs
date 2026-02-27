using Host.Services.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Host.Services.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();
    public DbSet<TelegramAccountEntity> TelegramAccounts => Set<TelegramAccountEntity>();
    public DbSet<SalaryOperationEntity> SalaryOperations => Set<SalaryOperationEntity>();
    public DbSet<SafeOperationEntity> SafeOperations => Set<SafeOperationEntity>();
    public DbSet<NonCashOperationEntity> NonCashOperations => Set<NonCashOperationEntity>();
    public DbSet<ExpenseEntity> Expenses => Set<ExpenseEntity>();
    public DbSet<RaportEntity> Raports => Set<RaportEntity>();
    public DbSet<RaportEmployeeEntity> RaportEmployees => Set<RaportEmployeeEntity>();
    public DbSet<RootPanelUserEntity> RootPanelUsers => Set<RootPanelUserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EmployeeEntity>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Name).IsRequired();
            builder.HasIndex(e => e.Name);
        });

        modelBuilder.Entity<TelegramAccountEntity>(builder =>
        {
            builder.HasKey(t => t.EmployeeId);
            builder.HasOne(t => t.Employee)
                .WithOne(e => e.TelegramAccount)
                .HasForeignKey<TelegramAccountEntity>(t => t.EmployeeId);
        });

        modelBuilder.Entity<SalaryOperationEntity>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.HasOne(s => s.Employee)
                .WithMany(e => e.SalaryOperations)
                .HasForeignKey(s => s.EmployeeId);
            builder.HasIndex(s => s.EmployeeId);
            builder.HasIndex(s => s.CreatedAt);
        });

        modelBuilder.Entity<SafeOperationEntity>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.HasIndex(s => s.CreatedAt);
        });

        modelBuilder.Entity<NonCashOperationEntity>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.HasIndex(s => s.CreatedAt);
        });

        modelBuilder.Entity<ExpenseEntity>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<RaportEntity>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.HasIndex(r => r.CreatedAt);
            builder.HasMany(r => r.Employees)
                .WithOne(e => e.Raport)
                .HasForeignKey(e => e.RaportId);
        });

        modelBuilder.Entity<RaportEmployeeEntity>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.EmployeeId);
        });

        modelBuilder.Entity<RootPanelUserEntity>(builder =>
        {
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Username).IsRequired();
            builder.Property(u => u.PasswordHash).IsRequired();
            builder.HasIndex(u => u.Username).IsUnique();
            builder.HasIndex(u => u.CreatedAt);
        });
    }
}
