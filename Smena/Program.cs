using Host.GrpcInterceptor;
using Host.Services;
using Host.Services.Data;
using Host.Services.Operations;
using Host.Services.Security;
using Host.Services.RootPanel;
using Host.Services.Telegram;
using Host.Services.Photo;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<PhotoOptions>(
    builder.Configuration.GetSection(PhotoOptions.SectionName));
builder.Services.Configure<ApiKeyOptions>(
    builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.Configure<RootPanelAuthOptions>(
    builder.Configuration.GetSection(RootPanelAuthOptions.SectionName));

builder.Services.AddSingleton<SafeUpdatesNotifier>();
builder.Services.AddSingleton<PhotoSessionStore>();
builder.Services.AddSingleton<TelegramUpdateOffsetStore>();
builder.Services.AddSingleton<IRootPanelAuthService, RootPanelAuthService>();

builder.Services.AddSingleton<ITelegramScopeAccessor, TelegramScopeAccessor>();

builder.Services.AddScoped<TelegramService>();
builder.Services.AddScoped<TelegramPhotoRequestService>();
builder.Services.AddScoped<SafeOperationsService>();
builder.Services.AddScoped<SalaryOperationsService>();

var app = builder.Build();

// Применяем миграции при старте
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync(); // <-- автоматически применяет миграции
        Console.WriteLine("Database migrated successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database migration failed: {ex.Message}");
        throw; // прерываем старт приложения, если БД недоступна
    }
}

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
