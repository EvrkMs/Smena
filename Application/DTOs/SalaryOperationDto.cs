using Domain.Models.Operations;

namespace Application.Services;

public partial class SalaryService
{
    public record SalaryOperationDto(
        int Amount,
        string Comment,
        SalaryOperationType Type,
        Guid EmployeeId);
}
