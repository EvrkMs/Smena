using Host;
using Host.GrpcInterceptor;
using Host.Services;
using Host.Services.Data;
using Host.Services.RootPanel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust only the local Docker/Traefik network.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.WebHost.UseKestrel(opt =>
{
    opt.ListenAnyIP(5001, optListner =>
    {
        // gRPC endpoint (h2c)
        optListner.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    opt.ListenAnyIP(5000, optListner =>
    {
        // Razor/admin endpoint
        optListner.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

// Ключи Data Protection (ими шифруются antiforgery-куки панели) — в Postgres.
// По умолчанию key ring живёт в файловой системе контейнера: swarm пересоздаёт
// контейнер, ключи теряются, и старые куки перестают расшифровываться
// ("The key {...} was not found in the key ring"). БД одна на все реплики,
// генерация и ротация ключей остаются полностью автоматическими.
builder.Services.AddDataProtection()
    .SetApplicationName("Smena")
    .PersistKeysToDbContext<AppDbContext>();

// Add services to the container.
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcApiKeyInterceptor>();
    options.Interceptors.Add<GrpcExceptionInterceptor>();
    options.Interceptors.Add<TelegramScopeInterceptor>();
});
builder.Services.AddHealthChecks();
builder.Services.AddRazorPages();
builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();
await app.Services.ApplyDatabaseMigrationsAsync();

// Caddy не стрипит путь — бэкенд сам читает префикс.
// Проверяем /panel и /grpc независимо от порта:
// пути не пересекаются (gRPC: /<Package>/<Method>, Razor: /root/...).
app.Use(async (context, next) =>
{
    foreach (var prefix in new[] { new PathString("/panel"), new PathString("/grpc") })
    {
        if (context.Request.Path.StartsWithSegments(prefix, out var remaining))
        {
            context.Request.PathBase = context.Request.PathBase.Add(prefix);
            context.Request.Path = remaining.HasValue ? remaining : PathString.Empty;
            break;
        }
    }
    await next();
});

// Явно вызываем UseRouting ПОСЛЕ нашего middleware,
// чтобы роуты матчились по уже изменённому Path.
app.UseRouting();

app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseMiddleware<RootPanelAuthMiddleware>();

// Configure the HTTP request pipeline.
app.MapGrpcService<GrpcConstantsService>();
app.MapGrpcService<GrpcEmployeeService>();
app.MapGrpcService<GrpcSafeService>();
app.MapGrpcService<GrpcExpenseService>();
app.MapGrpcService<GrpcAdvanceService>();
app.MapGrpcService<GrpcInventoryService>();
app.MapGrpcService<GrpcRaportService>();
app.MapGrpcService<GrpcSendPhotoService>();
app.MapGrpcService<GrpcWarehouseService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapHealthChecks("/healthz");
app.MapRazorPages();

app.Run();
