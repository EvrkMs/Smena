using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Safe;
using Host.Services.Operations;
using Host.Services.Telegram;

namespace Host.Services;

public class GrpcSafeService(
    SafeOperationsService safeOperationsService,
    ITelegramScopeAccessor scopeAccessor,
    IHostApplicationLifetime appLifetime)
    : Host.Grpc.Services.Safe.GrpcSafeService.GrpcSafeServiceBase
{
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;
    private readonly IHostApplicationLifetime _appLifetime = appLifetime;

    public override async Task<CurrentSafeResponse> CurrentSafe(Empty request, ServerCallContext context)
    {
        var currentSafe = await _safeOperationsService.GetCurrentSafeAsync(context.CancellationToken);
        return new CurrentSafeResponse { Current = currentSafe };
    }

    public override async Task<BoolResponse> AddSafeOperation(SafeOperationAdd request, ServerCallContext context)
    {
        if (request.Amount <= 0)
        {
            return new BoolResponse { Success = false, Message = "Invalid amount." };
        }

        if (request.Type == SafeOperationTypeGrpc.SafeOperationTypeUnspecified)
        {
            return new BoolResponse { Success = false, Message = "Operation type is required." };
        }

        var signedAmount = request.Type == SafeOperationTypeGrpc.Coming
            ? request.Amount
            : -request.Amount;

        var scope = _scopeAccessor.Current
            ?? throw new InvalidOperationException("Telegram scope is not available.");

        await _safeOperationsService.ApplyAndSaveAsync(
            signedAmount,
            request.Comment ?? string.Empty,
            scope,
            context.CancellationToken);

        return new BoolResponse { Success = true, Message = "Safe operation added." };
    }

    public override async Task SubscribeSafe(Empty request, IServerStreamWriter<CurrentSafeResponse> responseStream, ServerCallContext context)
    {
        var channel = _safeOperationsService.Subscribe();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken,
            _appLifetime.ApplicationStopping);
        var cancellationToken = linkedCts.Token;

        try
        {
            var currentSafe = await _safeOperationsService.GetCurrentSafeAsync(cancellationToken);
            await responseStream.WriteAsync(new CurrentSafeResponse { Current = currentSafe });

            await foreach (var value in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await responseStream.WriteAsync(new CurrentSafeResponse { Current = value });
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested || _appLifetime.ApplicationStopping.IsCancellationRequested)
        {
            // client disconnected or app is shutting down
        }
        finally
        {
            _safeOperationsService.Unsubscribe(channel);
        }
    }
}
