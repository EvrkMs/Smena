using Host.Services.Data;
using Host.Services.Operations;
using Host.Services.Photo;
using Host.Services.RootPanel;
using Host.Services.Security;
using Host.Services.Telegram;
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
        services.AddSingleton<TelegramUpdateOffsetStore>();
        services.AddSingleton<ITelegramScopeAccessor, TelegramScopeAccessor>();

        services.AddScoped<TelegramService>();
        services.AddScoped<TelegramPhotoRequestService>();
        services.AddScoped<SafeOperationsService>();
        services.AddScoped<SalaryOperationsService>();

        services.AddRootPanelServices(configuration);

        return services;
    }
}
