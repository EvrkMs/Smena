using Host.GrpcInterceptor;
using Host.Services;
using Host.Services.Data;
using Host.Services.Operations;
using Host.Services.Security;
using Host.Services.Telegram;
using Host.Services.Photo;
using Microsoft.EntityFrameworkCore;

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
    options.Interceptors.Add<GrpcApiKeyInterceptor>();
    options.Interceptors.Add<GrpcExceptionInterceptor>();
    options.Interceptors.Add<TelegramScopeInterceptor>();
});
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<PhotoOptions>(
    builder.Configuration.GetSection(PhotoOptions.SectionName));
builder.Services.Configure<ApiKeyOptions>(
    builder.Configuration.GetSection(ApiKeyOptions.SectionName));

builder.Services.AddSingleton<SafeUpdatesNotifier>();
builder.Services.AddSingleton<PhotoSessionStore>();
builder.Services.AddSingleton<TelegramUpdateOffsetStore>();

builder.Services.AddSingleton<ITelegramScopeAccessor, TelegramScopeAccessor>();

builder.Services.AddScoped<TelegramService>();
builder.Services.AddScoped<TelegramPhotoRequestService>();
builder.Services.AddScoped<SafeOperationsService>();
builder.Services.AddScoped<SalaryOperationsService>();

var app = builder.Build();

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

app.Run();
