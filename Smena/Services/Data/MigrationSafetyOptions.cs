namespace Host.Services.Data;

public sealed class MigrationSafetyOptions
{
    public const string SectionName = "MigrationSafety";
    public bool AllowDestructiveChanges { get; set; } = false;
}
