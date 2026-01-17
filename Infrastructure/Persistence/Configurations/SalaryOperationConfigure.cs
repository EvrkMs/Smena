using Infrastructure.Persistence.Configurations.Bases;
using Infrastructure.Persistence.Entities.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal class SalaryOperationEntityConfiguration : IEntityTypeConfiguration<SalaryOperationEntity>
{
    public void Configure(EntityTypeBuilder<SalaryOperationEntity> builder)
    {
        // Вызов базовой конфигурации
        new OperationBaseEntityConfiguration<SalaryOperationTypeEntity, int, SalaryOperationEntity>().Configure(builder);

        // Связь с Employee
        builder.HasOne(x => x.EmployeeEntity)
               .WithMany(x => x.SalaryOperationEntities)
               .HasForeignKey(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
