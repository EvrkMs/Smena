using Host.Services.RootPanel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Host.Pages.Root;

public class ChangePasswordModel(IRootPanelAuthService authService) : PageModel
{
    private readonly IRootPanelAuthService _authService = authService;

    [BindProperty]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        var accessToken = Request.Cookies[RootPanelAuthMiddleware.AccessCookieName] ?? string.Empty;
        if (!_authService.IsAccessTokenValid(accessToken))
        {
            RootPanelAuthMiddleware.ClearAuthCookies(Response);
            return Redirect("/root/login");
        }

        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
        {
            ModelState.AddModelError(string.Empty, "Новый пароль должен быть не короче 6 символов.");
            return Page();
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "Пароли не совпадают.");
            return Page();
        }

        if (!_authService.TryChangePassword(accessToken, CurrentPassword, NewPassword, out var tokenPair, out var errorMessage))
        {
            ModelState.AddModelError(string.Empty, errorMessage);
            return Page();
        }

        RootPanelAuthMiddleware.AppendAuthCookies(Response, tokenPair);
        return Redirect("/root");
    }
}
