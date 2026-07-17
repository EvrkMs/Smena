using Host.Services.Data;
using Host.Services.Operations;
using Host.Services.Photo;
using Host.Services.RootPanel;
using Host.Services.Security;
using Host.Services.Telegram;
using Host.Services.Warehouse;
using Microsoft.EntityFrameworkCore;

namespace Host;

internal static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        services.Configure<TelegramOptions>(
            configuration.GetSection(TelegramOptions.SectionName));
        services.Configure<PhotoOptions>(
            configuration.GetSection(PhotoOptions.SectionName));
        services.Configure<ApiKeyOptions>(
            configuration.GetSection(ApiKeyOptions.SectionName));
        services.Configure<MigrationSafetyOptions>(
            configuration.GetSection(MigrationSafetyOptions.SectionName));

        services.AddSingleton<SafeUpdatesNotifier>();
        services.AddSingleton<PhotoSessionStore>();
        services.AddHostedService<PhotoSessionCleanupService>();
        // Ежемесячная финализация журналов сейфа/ЗП (см. LedgerCompactionService).
        services.AddHostedService<LedgerCompactionService>();
        services.AddSingleton<ITelegramScopeAccessor, TelegramScopeAccessor>();

        // Один общий bot-клиент и один потребитель getUpdates на всё приложение.
        services.AddSingleton<TelegramBotClientFactory>();
        services.AddSingleton<TelegramUpdatesPoller>();
        services.AddHostedService(sp => sp.GetRequiredService<TelegramUpdatesPoller>());

        services.AddScoped<TelegramService>();
        services.AddScoped<TelegramPhotoRequestService>();
        services.AddScoped<SafeOperationsService>();
        services.AddScoped<SalaryOperationsService>();
        services.AddScoped<NonCashOperationsService>();
        services.AddScoped<AdvanceOperationsService>();
        services.AddScoped<ExpenseOperationsService>();
        services.AddScoped<RaportOperationsService>();
        services.AddScoped<InventoryOperationsService>();
        services.AddScoped<EmployeeOperationsService>();

        services.AddRootPanelServices(configuration);

        services.Configure<WarehouseOptions>(
            configuration.GetSection(WarehouseOptions.SectionName));
        services.AddHttpClient<SkladHttpClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });
        services.AddScoped<WarehouseService>();
        // Singleton cache: загружается при старте, переиспользуется всеми запросами
        services.AddSingleton<SkladCacheService>();
        services.AddHostedService(sp => sp.GetRequiredService<SkladCacheService>());

        return services;
    }
}
