namespace Host.Services.Photo;

public class PhotoOptions
{
    public const string SectionName = "Photo";

    public int RequestTimeoutSeconds { get; set; } = 60;
    public int SessionTtlSeconds { get; set; } = 120;
}
