using Microsoft.Extensions.Options;

namespace Host.Services.RootPanel;

internal sealed class RootPanelAuthService(
    IOptions<RootPanelAuthOptions> options,
    IRootPanelTokenStore tokenStore,
    IRootPanelUserStore userStore,
    IRootPanelPasswordService passwordService) : IRootPanelAuthService
{
    private readonly RootPanelAuthOptions _options = options.Value;
    private readonly IRootPanelTokenStore _tokenStore = tokenStore;
    private readonly IRootPanelUserStore _userStore = userStore;
    private readonly IRootPanelPasswordService _passwordService = passwordService;

    public bool TryLogin(string username, string password, out RootPanelTokenPair tokenPair, out bool mustChangePassword)
    {
        tokenPair = default!;
        mustChangePassword = false;

        var user = _userStore.GetOrCreateConfiguredRootUser();
        if (user == null)
        {
            return false;
        }

        if (!string.Equals(username, user.Username, StringComparison.Ordinal) ||
            !_passwordService.Verify(user, password))
        {
            return false;
        }

        mustChangePassword = user.MustChangePassword;
        tokenPair = IssueTokenPair(user.Id, mustChangePassword);
        return true;
    }

    public bool TryRefresh(string refreshToken, out RootPanelTokenPair tokenPair)
    {
        tokenPair = default!;
        if (!_tokenStore.TryGetValidRefreshToken(refreshToken, out _))
        {
            return false;
        }

        _tokenStore.RevokeByRefreshToken(refreshToken);

        var user = _userStore.GetOrCreateConfiguredRootUser();
        if (user == null)
        {
            return false;
        }

        tokenPair = IssueTokenPair(user.Id, user.MustChangePassword);
        return true;
    }

    public bool IsAccessTokenValid(string accessToken)
        => _tokenStore.TryGetValidAccessToken(accessToken, out _);

    public bool MustChangePassword(string accessToken)
        => _tokenStore.MustChangePassword(accessToken);

    public bool TryChangePassword(
        string accessToken,
        string currentPassword,
        string newPassword,
        out RootPanelTokenPair tokenPair,
        out string errorMessage)
    {
        tokenPair = default!;
        errorMessage = string.Empty;

        if (!_tokenStore.TryGetValidAccessToken(accessToken, out var accessState))
        {
            errorMessage = "Сессия истекла. Войдите снова.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            errorMessage = "Новый пароль должен быть не короче 6 символов.";
            return false;
        }

        var user = _userStore.GetById(accessState.UserId);
        if (user == null)
        {
            errorMessage = "Пользователь не найден.";
            return false;
        }

        if (!_passwordService.Verify(user, currentPassword))
        {
            errorMessage = "Текущий пароль указан неверно.";
            return false;
        }

        var newPasswordHash = _passwordService.Hash(user, newPassword);
        if (!_userStore.TryUpdatePassword(accessState.UserId, newPasswordHash))
        {
            errorMessage = "Пользователь не найден.";
            return false;
        }

        _tokenStore.RevokeByRefreshToken(accessState.RefreshToken);
        _tokenStore.RevokeAccessToken(accessToken);
        tokenPair = IssueTokenPair(accessState.UserId, mustChangePassword: false);
        return true;
    }

    public void RevokeByRefreshToken(string refreshToken)
        => _tokenStore.RevokeByRefreshToken(refreshToken);

    private RootPanelTokenPair IssueTokenPair(Guid userId, bool mustChangePassword)
    {
        return _tokenStore.Issue(
            userId,
            mustChangePassword,
            TimeSpan.FromMinutes(_options.AccessTokenTtlMinutes),
            TimeSpan.FromMinutes(_options.RefreshTokenTtlMinutes));
    }
}
