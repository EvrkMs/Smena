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
