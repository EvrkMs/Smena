using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Host.Grpc.Services.Warehouse;
using Host.Services.Warehouse;

namespace Host.Services;

public class GrpcWarehouseService(SkladCacheService cacheService)
    : Host.Grpc.Services.Warehouse.GrpcWarehouseService.GrpcWarehouseServiceBase
{
    public override Task<GrpcWarehouseItemsResponse> GetAllItems(
        Empty request,
        ServerCallContext context)
    {
        var response = new GrpcWarehouseItemsResponse
        {
            LastRefreshedUtc = cacheService.LastRefreshed != DateTime.MinValue
                ? cacheService.LastRefreshed.ToString("o")
                : string.Empty
        };
        response.Items.AddRange(cacheService.Items.Select(ToGrpc));
        return Task.FromResult(response);
    }

    public override Task<GrpcWarehouseItemsResponse> SearchItems(
        GrpcWarehouseSearchRequest request,
        ServerCallContext context)
    {
        var limit = request.Limit > 0 ? request.Limit : 20;
        var response = new GrpcWarehouseItemsResponse
        {
            LastRefreshedUtc = cacheService.LastRefreshed != DateTime.MinValue
                ? cacheService.LastRefreshed.ToString("o")
                : string.Empty
        };
        response.Items.AddRange(cacheService.Search(request.Query, limit).Select(ToGrpc));
        return Task.FromResult(response);
    }

    private static GrpcWarehouseItem ToGrpc(SkladCacheItem x) => new()
    {
        Name    = x.Name,
        Article = x.Article ?? string.Empty,
        Folder  = x.Folder  ?? string.Empty,
        Stock   = x.Stock,
    };
}
