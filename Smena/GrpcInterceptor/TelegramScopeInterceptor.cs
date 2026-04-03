using Grpc.Core;
using Grpc.Core.Interceptors;
using Host.Services.Telegram;
using Microsoft.Extensions.Logging;

namespace Host.GrpcInterceptor;

/// <summary>
/// Creates per-call TelegramMessageScope and rolls back messages on exceptions.
/// </summary>
public sealed class TelegramScopeInterceptor(
    TelegramService telegramService,
    ITelegramScopeAccessor scopeAccessor,
    ILogger<TelegramScopeInterceptor> logger)
    : Interceptor
{
    private readonly TelegramService _telegramService = telegramService;
    private readonly ITelegramScopeAccessor _scopeAccessor = scopeAccessor;
    private readonly ILogger<TelegramScopeInterceptor> _logger = logger;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var scope = _telegramService.CreateScope();
        _scopeAccessor.Current = scope;

        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            await RollbackSafeAsync(scope, ex);
            throw;
        }
        finally
        {
            _scopeAccessor.Current = null;
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var scope = _telegramService.CreateScope();
        _scopeAccessor.Current = scope;

        try
        {
            return await continuation(requestStream, context);
        }
        catch (Exception ex)
        {
            await RollbackSafeAsync(scope, ex);
            throw;
        }
        finally
        {
            _scopeAccessor.Current = null;
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var scope = _telegramService.CreateScope();
        _scopeAccessor.Current = scope;

        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex)
        {
            await RollbackSafeAsync(scope, ex);
            throw;
        }
        finally
        {
            _scopeAccessor.Current = null;
        }
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var scope = _telegramService.CreateScope();
        _scopeAccessor.Current = scope;

        try
        {
            await continuation(requestStream, responseStream, context);
        }
        catch (Exception ex)
        {
            await RollbackSafeAsync(scope, ex);
            throw;
        }
        finally
        {
            _scopeAccessor.Current = null;
        }
    }

    private async Task RollbackSafeAsync(TelegramMessageScope scope, Exception ex)
    {
        try
        {
            await scope.RollbackAsync();
        }
        catch (Exception rollbackEx)
        {
            _logger.LogWarning(rollbackEx, "Failed to rollback Telegram messages after error: {Message}", ex.Message);
        }
    }
}
