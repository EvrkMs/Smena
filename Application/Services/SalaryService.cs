using Application.Interface;
using Domain.Common;
using Domain.Models.Operations;

namespace Application.Services;

public partial class SalaryService(ISalaryOperationRepository salaryOperationRepository, IUnitOfWork unitOfWork)
{
    private readonly ISalaryOperationRepository _salaryOperationRepository = salaryOperationRepository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result> AddOperation(SalaryOperationDto dto)
    {
        try
        {
            var salaryOperation = new SalaryOperation()
            {
                Amount = dto.Amount,
                Comment = dto.Comment,
                Type = dto.Type,
                EmployeeId = dto.EmployeeId
            };

            var salaryAdd = await _salaryOperationRepository.AddAsync(salaryOperation);

            if (salaryAdd.IsSuccess)
            {
                await _unitOfWork.SaveChangesAsync();
                return Result.Ok();
            }

            return Result.Fail(salaryAdd.Message);
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }
    }
}
