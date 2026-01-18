using Host.GrpcInterceptor;
using Host.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseKestrel(opt =>
{
    opt.ListenAnyIP(5001, optListner =>
    {
        optListner.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

// Add services to the container.
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionInterceptor>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<GrpcSafeService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
