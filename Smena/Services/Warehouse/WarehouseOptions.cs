namespace Host.Services.Warehouse;

public sealed class WarehouseOptions
{
    public const string SectionName = "Warehouse";

    /// <summary>Enable МойСклад integration.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Authentication mode: Token or LoginPassword.</summary>
    public WarehouseAuthMode AuthMode { get; set; } = WarehouseAuthMode.Token;

    /// <summary>Bearer token (used when AuthMode = Token).</summary>
    public string? Token { get; set; }

    /// <summary>Login (used when AuthMode = LoginPassword).</summary>
    public string? Login { get; set; }

    /// <summary>Password (used when AuthMode = LoginPassword).</summary>
    public string? Password { get; set; }

    /// <summary>МойСклад API base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.moysklad.ru/api/remap/1.2";

    /// <summary>Max items to load per page (1–1000).</summary>
    public int Limit { get; set; } = 500;

    /// <summary>How often (minutes) to refresh the local items cache. Default: 30.</summary>
    public int CacheRefreshMinutes { get; set; } = 30;
}

public enum WarehouseAuthMode
{
    Token,
    LoginPassword
}
