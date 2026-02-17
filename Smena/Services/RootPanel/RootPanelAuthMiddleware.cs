namespace Host.Services.RootPanel;

public sealed class RootPanelAuthMiddleware(
    RequestDelegate next,
    IRootPanelAuthService authService)
{
    public const string AccessCookieName = "smena_root_access";
    public const string RefreshCookieName = "smena_root_refresh";

    private readonly RequestDelegate _next = next;
    private readonly IRootPanelAuthService _authService = authService;

    public async Task Invoke(HttpContext context)
    {
        if (!IsRootPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (IsAnonymousEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var accessToken = context.Request.Cookies[AccessCookieName];
        if (_authService.IsAccessTokenValid(accessToken ?? string.Empty))
        {
            await _next(context);
            return;
        }

        var refreshToken = context.Request.Cookies[RefreshCookieName];
        if (_authService.TryRefresh(refreshToken ?? string.Empty, out var tokenPair))
        {
            AppendAuthCookies(context.Response, tokenPair);
            await _next(context);
            return;
        }

        ClearAuthCookies(context.Response);
        context.Response.Redirect("/root/login");
    }

    public static void AppendAuthCookies(HttpResponse response, RootPanelTokenPair tokenPair)
    {
        var accessOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Lax,
            Expires = tokenPair.AccessExpiresAtUtc
        };

        var refreshOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Lax,
            Expires = tokenPair.RefreshExpiresAtUtc
        };

        response.Cookies.Append(AccessCookieName, tokenPair.AccessToken, accessOptions);
        response.Cookies.Append(RefreshCookieName, tokenPair.RefreshToken, refreshOptions);
    }

    public static void ClearAuthCookies(HttpResponse response)
    {
        response.Cookies.Delete(AccessCookieName);
        response.Cookies.Delete(RefreshCookieName);
    }

    private static bool IsRootPath(PathString path)
        => path.StartsWithSegments("/root", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnonymousEndpoint(PathString path)
        => path.StartsWithSegments("/root/login", StringComparison.OrdinalIgnoreCase);
}
