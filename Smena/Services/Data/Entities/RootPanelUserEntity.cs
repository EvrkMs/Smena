namespace Host.Services.Data.Entities;

public class RootPanelUserEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "root";
    public string PasswordHash { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
