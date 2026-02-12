namespace Host.Services.Security;

public class ApiKeyOptions
{
    public const string SectionName = "ApiKey";
    public const string HeaderName = "x-api-key";

    public string? Key { get; set; }
}
