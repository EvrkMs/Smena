using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Host.Services.RootPanel;

public interface IRootPanelAuthService
{
    bool TryLogin(string username, string password, out RootPanelTokenPair tokenPair);
    bool TryRefresh(string refreshToken, out RootPanelTokenPair tokenPair);
    bool IsAccessTokenValid(string accessToken);
    void RevokeByRefreshToken(string refreshToken);
}

public sealed record RootPanelTokenPair(
    string AccessToken,
    DateTimeOffset AccessExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAtUtc);

public sealed class RootPanelAuthService(IOptions<RootPanelAuthOptions> options) : IRootPanelAuthService
{
    private sealed record AccessTokenState(
        string RefreshToken,
        DateTimeOffset ExpiresAtUtc);

    private sealed record RefreshTokenState(
        DateTimeOffset ExpiresAtUtc);

    private readonly RootPanelAuthOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, AccessTokenState> _accessTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RefreshTokenState> _refreshTokens = new(StringComparer.Ordinal);

    public bool TryLogin(string username, string password, out RootPanelTokenPair tokenPair)
    {
        tokenPair = default!;

        if (!string.Equals(username, _options.Username, StringComparison.Ordinal) ||
            !string.Equals(password, _options.Password, StringComparison.Ordinal))
        {
            return false;
        }

        tokenPair = CreateTokenPair();
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
        tokenPair = CreateTokenPair();
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

    private RootPanelTokenPair CreateTokenPair()
    {
        CleanupExpiredTokens();

        var now = DateTimeOffset.UtcNow;
        var accessExpiresAtUtc = now.AddMinutes(_options.AccessTokenTtlMinutes);
        var refreshExpiresAtUtc = now.AddMinutes(_options.RefreshTokenTtlMinutes);

        var refreshToken = CreateToken();
        var accessToken = CreateToken();

        _refreshTokens[refreshToken] = new RefreshTokenState(refreshExpiresAtUtc);
        _accessTokens[accessToken] = new AccessTokenState(refreshToken, accessExpiresAtUtc);

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
}
