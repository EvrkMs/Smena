using Microsoft.AspNetCore.WebUtilities;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Host.Services.RootPanel;

internal interface IRootPanelTokenStore
{
    RootPanelTokenPair Issue(Guid userId, bool mustChangePassword, TimeSpan accessTokenTtl, TimeSpan refreshTokenTtl);
    bool TryGetValidAccessToken(string accessToken, out RootPanelAccessTokenState accessTokenState);
    bool TryGetValidRefreshToken(string refreshToken, out RootPanelRefreshTokenState refreshTokenState);
    bool MustChangePassword(string accessToken);
    void RevokeByRefreshToken(string refreshToken);
    void RevokeAccessToken(string accessToken);
}

internal sealed record RootPanelAccessTokenState(
    Guid UserId,
    bool MustChangePassword,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);

internal sealed record RootPanelRefreshTokenState(
    DateTimeOffset ExpiresAtUtc);

internal sealed class InMemoryRootPanelTokenStore : IRootPanelTokenStore
{
    private readonly ConcurrentDictionary<string, RootPanelAccessTokenState> _accessTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RootPanelRefreshTokenState> _refreshTokens = new(StringComparer.Ordinal);

    public RootPanelTokenPair Issue(
        Guid userId,
        bool mustChangePassword,
        TimeSpan accessTokenTtl,
        TimeSpan refreshTokenTtl)
    {
        CleanupExpiredTokens();

        var now = DateTimeOffset.UtcNow;
        var accessExpiresAtUtc = now.Add(accessTokenTtl);
        var refreshExpiresAtUtc = now.Add(refreshTokenTtl);

        var refreshToken = CreateToken();
        var accessToken = CreateToken();

        _refreshTokens[refreshToken] = new RootPanelRefreshTokenState(refreshExpiresAtUtc);
        _accessTokens[accessToken] = new RootPanelAccessTokenState(userId, mustChangePassword, refreshToken, accessExpiresAtUtc);

        return new RootPanelTokenPair(
            accessToken,
            accessExpiresAtUtc,
            refreshToken,
            refreshExpiresAtUtc);
    }

    public bool TryGetValidAccessToken(string accessToken, out RootPanelAccessTokenState accessTokenState)
    {
        accessTokenState = default!;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        if (!_accessTokens.TryGetValue(accessToken, out var state))
        {
            return false;
        }

        if (state.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            accessTokenState = state;
            return true;
        }

        _accessTokens.TryRemove(accessToken, out _);
        return false;
    }

    public bool TryGetValidRefreshToken(string refreshToken, out RootPanelRefreshTokenState refreshTokenState)
    {
        refreshTokenState = default!;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        if (!_refreshTokens.TryGetValue(refreshToken, out var state))
        {
            return false;
        }

        if (state.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            refreshTokenState = state;
            return true;
        }

        RevokeByRefreshToken(refreshToken);
        return false;
    }

    public bool MustChangePassword(string accessToken)
        => TryGetValidAccessToken(accessToken, out var accessState) && accessState.MustChangePassword;

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

    public void RevokeAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        _accessTokens.TryRemove(accessToken, out _);
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
