using Infrastructure.Persistence.Entities.Operations.Bases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Numerics;

namespace Infrastructure.Persistence.Configurations.Bases;

internal class OperationBaseEntityConfiguration<TType, TAmount, TEntity> : IEntityTypeConfiguration<TEntity>
    where TType : Enum
    where TAmount : INumber<TAmount>
    where TEntity : OperationBaseEntity<TType, TAmount>
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        // Id
        builder.HasKey(x => x.Id);

        // Колонки
        builder.Property(x => x.Amount).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(500);

        // Можно добавить common index
        builder.HasIndex(x => x.Type);
    }
}
