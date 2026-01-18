using Application.Services;
using Domain.Common;
using Domain.Models.Operations;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Service.Safe;
using static Application.Services.SafeService;

namespace Host.Services;

public class GrpcSafeService(SafeService safeService) : SafeServiceGrpc.SafeServiceGrpcBase
{
    private readonly SafeService _safeService = safeService;

    public override async Task<Empty> AddOperationSafe(SafeOperationAdd request, ServerCallContext context)
    {
        var result = await _safeService.AddOperation(new SafeOperationDto(
            Amount: request.Amount,
            Comment: request.Comment,
            Type: MapGrpcToDomain(request.Type)));

        if (!result.IsSuccess)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                result.Message));
        }

        return new Empty();
    }

    private static SafeOperationType MapGrpcToDomain(SafeOperationTypeGrpc type)
        => type switch
        {
            SafeOperationTypeGrpc.Coming => SafeOperationType.Coming,
            SafeOperationTypeGrpc.Exponse => SafeOperationType.Exponse,
            _ => throw new NotSupportedException(
                $"Unsupported SafeOperationTypeGrpc: {type}")
        };
}