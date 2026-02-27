using Host;
using Host.GrpcInterceptor;
using Host.Services;
using Host.Services.Data;
using Host.Services.RootPanel;

var builder = WebApplication.CreateBuilder(args);

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

app.UseMiddleware<RootPanelAuthMiddleware>();

// Configure the HTTP request pipeline.
app.MapGrpcService<GrpcEmployeeService>();
app.MapGrpcService<GrpcSafeService>();
app.MapGrpcService<GrpcExpenseService>();
app.MapGrpcService<GrpcAdvanceService>();
app.MapGrpcService<GrpcInventoryService>();
app.MapGrpcService<GrpcRaportService>();
app.MapGrpcService<GrpcSendPhotoService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapHealthChecks("/healthz");
app.MapRazorPages();

app.Run();
