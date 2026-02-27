using Host.Services.RootPanel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Host.Pages.Root;

public class LoginModel(IRootPanelAuthService authService) : PageModel
{
    private readonly IRootPanelAuthService _authService = authService;

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        if (!_authService.TryLogin(Username, Password, out var tokenPair, out var mustChangePassword))
        {
            ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
            return Page();
        }

        RootPanelAuthMiddleware.AppendAuthCookies(Response, tokenPair);
        return Redirect(mustChangePassword ? "/root/change-password" : "/root");
    }
}
