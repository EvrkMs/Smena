using Infrastructure.Persistence.Configurations.Bases;
using Infrastructure.Persistence.Entities.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class SafeOperationConfigure : IEntityTypeConfiguration<SafeOperationEntity>
{
    public void Configure(EntityTypeBuilder<SafeOperationEntity> builder)
    {
        // Вызов базовой конфигурации
        new OperationBaseEntityConfiguration<SafeOperationTypeEntity, int, SafeOperationEntity>().Configure(builder);
    }
}
