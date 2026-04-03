using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Host.GrpcInterceptor;

public class GrpcExceptionInterceptor(ILogger<GrpcExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(requestStream, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(requestStream, responseStream, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    private RpcException MapException(Exception ex)
    {
        if (ex is RpcException rpc)
            return rpc;

        logger.LogError(ex, "Unhandled gRPC exception in {Method}", ex.TargetSite?.Name);

        return ex switch
        {
            InvalidOperationException => new RpcException(
                new Status(StatusCode.FailedPrecondition, ex.Message)),
            NotSupportedException => new RpcException(
                new Status(StatusCode.InvalidArgument, ex.Message)),
            _ => new RpcException(
                new Status(StatusCode.Internal, "Internal server error"))
        };
    }
}
