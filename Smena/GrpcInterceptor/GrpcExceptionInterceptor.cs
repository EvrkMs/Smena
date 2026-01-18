using Domain.Common;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Host.GrpcInterceptor
{
    public class GrpcExceptionInterceptor : Interceptor
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
            catch (DomainException ex)
            {
                throw new RpcException(
                    new Status(StatusCode.FailedPrecondition, ex.Message));
            }
            catch (NotSupportedException ex)
            {
                throw new RpcException(
                    new Status(StatusCode.InvalidArgument, ex.Message));
            }
            catch (RpcException)
            {
                // если уже RpcException — не трогаем
                throw;
            }
            catch (Exception)
            {
                // логировать обязательно
                throw new RpcException(
                    new Status(StatusCode.Internal, "Internal server error"));
            }
        }
    }
}