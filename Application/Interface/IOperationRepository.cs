using Domain.Common;
using Domain.Models.Operations;
using Domain.Models.Operations.Base;
using System.Numerics;

namespace Application.Interface;

public interface IOperationRepository<TType, TAmount, TOperation>
    where TType : Enum
    where TAmount : INumber<TAmount>
    where TOperation : OperationBase<TType, TAmount>
{
    Task<Result<TOperation>> AddAsync(TOperation operation);
}
public interface ISafeOperationRepository : IOperationRepository<SafeOperationType, int, SafeOperation> { }

public interface ISalaryOperationRepository : IOperationRepository<SalaryOperationType, int, SalaryOperation>
{
    Task<int> GetSalaryByEmployeeId(Guid id);
}
