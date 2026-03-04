using Host.Services.RootPanel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Host.Pages.Root;

public class LoginModel(
    IRootPanelAuthService authService,
    IRootPanelLoginRateLimiter loginRateLimiter) : PageModel
{
    private readonly IRootPanelAuthService _authService = authService;
    private readonly IRootPanelLoginRateLimiter _loginRateLimiter = loginRateLimiter;

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        var clientKey = ResolveClientKey();
        if (_loginRateLimiter.TryGetRetryAfter(clientKey, out var retryAfter))
        {
            ModelState.AddModelError(
                string.Empty,
                $"Слишком много попыток входа. Повторите через {FormatRetryAfter(retryAfter)}.");
            return Page();
        }

        if (!_authService.TryLogin(Username, Password, out var tokenPair, out var mustChangePassword))
        {
            _loginRateLimiter.RegisterFailure(clientKey);

            if (_loginRateLimiter.TryGetRetryAfter(clientKey, out retryAfter))
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"Слишком много попыток входа. Повторите через {FormatRetryAfter(retryAfter)}.");
                return Page();
            }

            ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
            return Page();
        }

        _loginRateLimiter.RegisterSuccess(clientKey);
        RootPanelAuthMiddleware.AppendAuthCookies(Response, tokenPair);
        return Redirect(mustChangePassword ? "/root/change-password" : "/root");
    }

    private string ResolveClientKey()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstIp = forwardedFor
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(firstIp))
            {
                return firstIp;
            }
        }

        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(realIp))
        {
            return realIp.Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string FormatRetryAfter(TimeSpan retryAfter)
    {
        var totalSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        if (totalSeconds < 60)
        {
            return $"{totalSeconds} сек.";
        }

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return seconds == 0
            ? $"{minutes} мин."
            : $"{minutes} мин. {seconds} сек.";
    }
}
