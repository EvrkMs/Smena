using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Persistence.Entities.Bases;

public class Entity<TId>
    where TId : struct
{
    [Key]
    public TId Id { get; set; }
}
