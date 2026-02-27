using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.Extensions.Options;

namespace Host.Services.RootPanel;

internal interface IRootPanelUserStore
{
    RootPanelUserEntity? GetOrCreateConfiguredRootUser();
    RootPanelUserEntity? GetById(Guid userId);
    bool TryUpdatePassword(Guid userId, string passwordHash);
}

internal sealed class RootPanelUserStore(
    IOptions<RootPanelAuthOptions> options,
    IServiceScopeFactory scopeFactory,
    IRootPanelPasswordService passwordService) : IRootPanelUserStore
{
    private readonly RootPanelAuthOptions _options = options.Value;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IRootPanelPasswordService _passwordService = passwordService;

    public RootPanelUserEntity? GetOrCreateConfiguredRootUser()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = db.RootPanelUsers.FirstOrDefault(u => u.Username == _options.Username);
        if (user != null)
        {
            return user;
        }

        var created = new RootPanelUserEntity
        {
            Username = _options.Username,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        created.PasswordHash = _passwordService.Hash(created, _options.Password);

        db.RootPanelUsers.Add(created);
        db.SaveChanges();
        return created;
    }

    public RootPanelUserEntity? GetById(Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.RootPanelUsers.FirstOrDefault(u => u.Id == userId);
    }

    public bool TryUpdatePassword(Guid userId, string passwordHash)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.RootPanelUsers.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return false;
        }

        user.PasswordHash = passwordHash;
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();
        return true;
    }
}
