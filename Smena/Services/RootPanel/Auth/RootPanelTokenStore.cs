using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
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
    DateTimeOffset ExpiresAtUtc,
    string AccessToken);

/// <summary>
/// Токены живут в IMemoryCache с AbsoluteExpiration — истечение и очистку
/// делает сам кэш. Раньше это был ручной ConcurrentDictionary: просроченные
/// токены копились, пока кто-нибудь не залогинится (Issue), а чистка была
/// O(refresh × access) — на живущей неделями refresh-сессии память текла,
/// а редкий логин платил квадратичный скан.
/// </summary>
internal sealed class InMemoryRootPanelTokenStore(IMemoryCache cache) : IRootPanelTokenStore
{
    // Кэш общий на процесс — ключи токенов не должны пересечься с чужими записями.
    private const string AccessPrefix = "rootpanel:access:";
    private const string RefreshPrefix = "rootpanel:refresh:";

    private readonly IMemoryCache _cache = cache;

    public RootPanelTokenPair Issue(
        Guid userId,
        bool mustChangePassword,
        TimeSpan accessTokenTtl,
        TimeSpan refreshTokenTtl)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpiresAtUtc = now.Add(accessTokenTtl);
        var refreshExpiresAtUtc = now.Add(refreshTokenTtl);

        var refreshToken = CreateToken();
        var accessToken = CreateToken();

        _cache.Set(
            AccessPrefix + accessToken,
            new RootPanelAccessTokenState(userId, mustChangePassword, refreshToken, accessExpiresAtUtc),
            new MemoryCacheEntryOptions { AbsoluteExpiration = accessExpiresAtUtc });

        _cache.Set(
            RefreshPrefix + refreshToken,
            new RootPanelRefreshTokenState(refreshExpiresAtUtc, accessToken),
            new MemoryCacheEntryOptions { AbsoluteExpiration = refreshExpiresAtUtc });

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

        if (!_cache.TryGetValue(AccessPrefix + accessToken, out RootPanelAccessTokenState? state) || state is null)
        {
            return false;
        }

        accessTokenState = state;
        return true;
    }

    public bool TryGetValidRefreshToken(string refreshToken, out RootPanelRefreshTokenState refreshTokenState)
    {
        refreshTokenState = default!;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        if (!_cache.TryGetValue(RefreshPrefix + refreshToken, out RootPanelRefreshTokenState? state) || state is null)
        {
            return false;
        }

        refreshTokenState = state;
        return true;
    }

    public bool MustChangePassword(string accessToken)
        => TryGetValidAccessToken(accessToken, out var accessState) && accessState.MustChangePassword;

    public void RevokeByRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        // Связанный access-токен хранится в состоянии refresh-а — скан всех
        // access-записей больше не нужен.
        if (_cache.TryGetValue(RefreshPrefix + refreshToken, out RootPanelRefreshTokenState? state) && state is not null)
        {
            _cache.Remove(AccessPrefix + state.AccessToken);
        }

        _cache.Remove(RefreshPrefix + refreshToken);
    }

    public void RevokeAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        _cache.Remove(AccessPrefix + accessToken);
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return WebEncoders.Base64UrlEncode(bytes);
    }
}
