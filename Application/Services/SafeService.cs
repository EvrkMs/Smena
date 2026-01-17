using Application.Interface;
using Domain.Common;
using Domain.Models.Operations;

namespace Application.Services;

public class SafeService(
    ISafeOperationRepository safeOperationRepository,
    IUnitOfWork unitOfWork)
{
    private readonly ISafeOperationRepository _safeOperationRepository = safeOperationRepository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public record SafeOperationDto(
        int Amount,
        string Comment,
        SafeOperationType Type);
    public async Task<Result<SafeOperation>> AddOperation(SafeOperationDto dto)
    {
        try
        {
            var safeOperation = new SafeOperation
            {
                Amount = dto.Amount,
                Comment = dto.Comment,
                Type = dto.Type
            };

            var safeOperationAdd = await _safeOperationRepository.AddAsync(safeOperation);
            if (!safeOperationAdd.IsSuccess)
            {
                return safeOperationAdd;
            }

            await _unitOfWork.SaveChangesAsync();

            return safeOperationAdd;
        }
        catch (DomainException ex)
        {
            return Result<SafeOperation>.Fail("Error in operation add SafeOperation\n" + ex.Message);
        }

    }
}
