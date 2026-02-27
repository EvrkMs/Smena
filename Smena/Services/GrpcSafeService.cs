using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Host.Grpc.Common;
using Host.Grpc.Services.Safe;
using Host.Services.Data;
using Host.Services.Operations;
using Host.Services.Telegram;

namespace Host.Services;

public class GrpcSafeService(
    AppDbContext db,
    SafeOperationsService safeOperationsService,
    SafeUpdatesNotifier safeUpdatesNotifier,
    ITelegramScopeAccessor scopeAccessor)
    : Host.Grpc.Services.Safe.GrpcSafeService.GrpcSafeServiceBase
{
    private readonly AppDbContext _db = db;
    private readonly SafeOperationsService _safeOperationsService = safeOperationsService;
    private readonly SafeUpdatesNotifier _safeUpdatesNotifier = safeUpdatesNotifier;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;

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

        var scope = _scopeAccessor.Current ?? throw new InvalidOperationException("Telegram scope is not available.");

        int? updatedSafe = null;
        await TransactionHelper.ExecuteAsync(_db, async () =>
        {
            var comment = request.Comment ?? string.Empty;
            updatedSafe = await _safeOperationsService.ApplySafeOperationAsync(
                signedAmount,
                comment,
                scope,
                context.CancellationToken);

            await _db.SaveChangesAsync(context.CancellationToken);
        }, context.CancellationToken);

        if (updatedSafe.HasValue)
        {
            _safeUpdatesNotifier.Publish(updatedSafe.Value);
        }

        return new BoolResponse { Success = true, Message = "Safe operation added." };
    }

    public override async Task SubscribeSafe(Empty request, IServerStreamWriter<CurrentSafeResponse> responseStream, ServerCallContext context)
    {
        var channel = _safeUpdatesNotifier.Subscribe();

        try
        {
            // send initial value
            var currentSafe = await _safeOperationsService.GetCurrentSafeAsync(context.CancellationToken);
            await responseStream.WriteAsync(new CurrentSafeResponse { Current = currentSafe });

            await foreach (var value in channel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(new CurrentSafeResponse { Current = value });
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
        finally
        {
            _safeUpdatesNotifier.Unsubscribe(channel);
        }
    }

}
