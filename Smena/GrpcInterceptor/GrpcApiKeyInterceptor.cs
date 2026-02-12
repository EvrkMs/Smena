using Grpc.Core;
using Grpc.Core.Interceptors;
using Host.Services.Security;
using Microsoft.Extensions.Options;

namespace Host.GrpcInterceptor;

public class GrpcApiKeyInterceptor(IOptions<ApiKeyOptions> options) : Interceptor
{
    private readonly ApiKeyOptions _options = options.Value;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        return await continuation(request, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        await continuation(request, responseStream, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        return await continuation(requestStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        await continuation(requestStream, responseStream, context);
    }

    private void ValidateApiKey(ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(_options.Key))
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "API key is not configured."));
        }

        var actual = context.RequestHeaders
            .FirstOrDefault(h => h.Key == ApiKeyOptions.HeaderName)
            ?.Value;

        if (!string.Equals(actual, _options.Key, StringComparison.Ordinal))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Invalid API key."));
        }
    }
}
