using Host.Services.Data;
using Host.Services.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Host.Services.RootPanel;

public interface IRootPanelAuthService
{
    bool TryLogin(string username, string password, out RootPanelTokenPair tokenPair, out bool mustChangePassword);
    bool TryRefresh(string refreshToken, out RootPanelTokenPair tokenPair);
    bool IsAccessTokenValid(string accessToken);
    bool MustChangePassword(string accessToken);
    bool TryChangePassword(string accessToken, string currentPassword, string newPassword, out RootPanelTokenPair tokenPair, out string errorMessage);
    void RevokeByRefreshToken(string refreshToken);
}

public sealed record RootPanelTokenPair(
    string AccessToken,
    DateTimeOffset AccessExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAtUtc);

public sealed class RootPanelAuthService(
    IOptions<RootPanelAuthOptions> options,
    IServiceScopeFactory scopeFactory) : IRootPanelAuthService
{
    private sealed record AccessTokenState(
        Guid UserId,
        bool MustChangePassword,
        string RefreshToken,
        DateTimeOffset ExpiresAtUtc);

    private sealed record RefreshTokenState(
        DateTimeOffset ExpiresAtUtc);

    private readonly RootPanelAuthOptions _options = options.Value;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ConcurrentDictionary<string, AccessTokenState> _accessTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RefreshTokenState> _refreshTokens = new(StringComparer.Ordinal);

    public bool TryLogin(string username, string password, out RootPanelTokenPair tokenPair, out bool mustChangePassword)
    {
        tokenPair = default!;
        mustChangePassword = false;

        var user = GetOrCreateRootUser();
        if (user == null)
        {
            return false;
        }

        if (!string.Equals(username, user.Username, StringComparison.Ordinal) ||
            !VerifyPassword(password, user.PasswordHash))
        {
            return false;
        }

        mustChangePassword = user.MustChangePassword;
        tokenPair = CreateTokenPair(user.Id, mustChangePassword);
        return true;
    }

    public bool TryRefresh(string refreshToken, out RootPanelTokenPair tokenPair)
    {
        tokenPair = default!;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        if (!_refreshTokens.TryGetValue(refreshToken, out var refreshState))
        {
            return false;
        }

        if (refreshState.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            RevokeByRefreshToken(refreshToken);
            return false;
        }

        RevokeByRefreshToken(refreshToken);

        var user = GetOrCreateRootUser();
        if (user == null)
        {
            return false;
        }

        tokenPair = CreateTokenPair(user.Id, user.MustChangePassword);
        return true;
    }

    public bool IsAccessTokenValid(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        if (!_accessTokens.TryGetValue(accessToken, out var accessState))
        {
            return false;
        }

        if (accessState.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _accessTokens.TryRemove(accessToken, out _);
        return false;
    }

    public bool MustChangePassword(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        if (!_accessTokens.TryGetValue(accessToken, out var accessState))
        {
            return false;
        }

        return accessState.ExpiresAtUtc > DateTimeOffset.UtcNow && accessState.MustChangePassword;
    }

    public bool TryChangePassword(
        string accessToken,
        string currentPassword,
        string newPassword,
        out RootPanelTokenPair tokenPair,
        out string errorMessage)
    {
        tokenPair = default!;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(accessToken) ||
            !_accessTokens.TryGetValue(accessToken, out var accessState) ||
            accessState.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            errorMessage = "Session expired. Please login again.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            errorMessage = "New password must be at least 6 characters.";
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.RootPanelUsers.FirstOrDefault(u => u.Id == accessState.UserId);
        if (user == null)
        {
            errorMessage = "User not found.";
            return false;
        }

        if (!VerifyPassword(currentPassword, user.PasswordHash))
        {
            errorMessage = "Current password is invalid.";
            return false;
        }

        user.PasswordHash = HashPassword(newPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();

        RevokeByRefreshToken(accessState.RefreshToken);
        _accessTokens.TryRemove(accessToken, out _);
        tokenPair = CreateTokenPair(user.Id, mustChangePassword: false);
        return true;
    }

    public void RevokeByRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        _refreshTokens.TryRemove(refreshToken, out _);

        foreach (var kv in _accessTokens)
        {
            if (!string.Equals(kv.Value.RefreshToken, refreshToken, StringComparison.Ordinal))
            {
                continue;
            }

            _accessTokens.TryRemove(kv.Key, out _);
        }
    }

    private RootPanelTokenPair CreateTokenPair(Guid userId, bool mustChangePassword)
    {
        CleanupExpiredTokens();

        var now = DateTimeOffset.UtcNow;
        var accessExpiresAtUtc = now.AddMinutes(_options.AccessTokenTtlMinutes);
        var refreshExpiresAtUtc = now.AddMinutes(_options.RefreshTokenTtlMinutes);

        var refreshToken = CreateToken();
        var accessToken = CreateToken();

        _refreshTokens[refreshToken] = new RefreshTokenState(refreshExpiresAtUtc);
        _accessTokens[accessToken] = new AccessTokenState(userId, mustChangePassword, refreshToken, accessExpiresAtUtc);

        return new RootPanelTokenPair(
            accessToken,
            accessExpiresAtUtc,
            refreshToken,
            refreshExpiresAtUtc);
    }

    private void CleanupExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var kv in _accessTokens)
        {
            if (kv.Value.ExpiresAtUtc <= now)
            {
                _accessTokens.TryRemove(kv.Key, out _);
            }
        }

        foreach (var kv in _refreshTokens)
        {
            if (kv.Value.ExpiresAtUtc <= now)
            {
                RevokeByRefreshToken(kv.Key);
            }
        }
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    private RootPanelUserEntity? GetOrCreateRootUser()
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
            PasswordHash = HashPassword(_options.Password),
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.RootPanelUsers.Add(created);
        db.SaveChanges();
        return created;
    }

    private static bool VerifyPassword(string rawPassword, string hash)
        => string.Equals(HashPassword(rawPassword), hash, StringComparison.Ordinal);

    private static string HashPassword(string rawPassword)
    {
        var bytes = Encoding.UTF8.GetBytes(rawPassword);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
