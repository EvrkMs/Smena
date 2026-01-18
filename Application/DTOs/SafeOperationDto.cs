using Domain.Models.Operations;

namespace Application.Services;

public partial class SafeService
{
    public record SafeOperationDto(
        int Amount,
        string Comment,
        SafeOperationType Type);
}
