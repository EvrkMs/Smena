namespace Host.Services.RootPanel;

public sealed class RootPanelAuthOptions
{
    public const string SectionName = "RootPanelAuth";

    public string Username { get; set; } = "root";
    public string Password { get; set; } = "root";
    public int AccessTokenTtlMinutes { get; set; } = 5;
    public int RefreshTokenTtlMinutes { get; set; } = 30;
}
