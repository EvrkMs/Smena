using Host.Services.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace Host.Services.RootPanel;

public static class RootPanelServiceCollectionExtensions
{
    public static IServiceCollection AddRootPanelServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RootPanelAuthOptions>(
            configuration.GetSection(RootPanelAuthOptions.SectionName));
        services.Configure<RootPanelLoginRateLimitOptions>(
            configuration.GetSection(RootPanelLoginRateLimitOptions.SectionName));

        // Хранилище токенов живёт поверх IMemoryCache (TTL-очистку делает кэш).
        services.AddMemoryCache();

        services.AddSingleton<IPasswordHasher<RootPanelUserEntity>, PasswordHasher<RootPanelUserEntity>>();
        services.AddSingleton<IRootPanelLoginRateLimiter, InMemoryRootPanelLoginRateLimiter>();
        services.AddSingleton<IRootPanelPasswordService, RootPanelPasswordService>();
        services.AddSingleton<IRootPanelTokenStore, InMemoryRootPanelTokenStore>();
        services.AddSingleton<IRootPanelUserStore, RootPanelUserStore>();
        services.AddSingleton<IRootPanelAuthService, RootPanelAuthService>();

        return services;
    }
}
