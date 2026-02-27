using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Host.Services.Data;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrationSafety = scope.ServiceProvider.GetRequiredService<IOptions<MigrationSafetyOptions>>().Value;

        try
        {
            await EnsureNoDestructiveMigrationAsync(db, migrationSafety);
            await db.Database.MigrateAsync();
            Console.WriteLine("Database migrated successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database migration failed: {ex.Message}");
            throw;
        }
    }

    private static async Task EnsureNoDestructiveMigrationAsync(AppDbContext db, MigrationSafetyOptions migrationSafety)
    {
        if (migrationSafety.AllowDestructiveChanges)
        {
            return;
        }

        var pending = await db.Database.GetPendingMigrationsAsync();
        if (!pending.Any())
        {
            return;
        }

        var applied = await db.Database.GetAppliedMigrationsAsync();
        var fromMigration = applied.LastOrDefault();
        var toMigration = pending.Last();

        var migrator = db.GetService<IMigrator>();
        var script = migrator.GenerateScript(fromMigration, toMigration);

        if (LooksDestructive(script))
        {
            throw new InvalidOperationException(
                "Destructive migration detected (DROP TABLE/COLUMN/SCHEMA/CONSTRAINT or RENAME COLUMN). " +
                "Set MigrationSafety:AllowDestructiveChanges=true to allow this migration.");
        }
    }

    private static bool LooksDestructive(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        return Regex.IsMatch(sql, @"\bDROP\s+(TABLE|COLUMN|SCHEMA|CONSTRAINT)\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(sql, @"\bALTER\s+TABLE\b[\s\S]*?\bDROP\s+COLUMN\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(sql, @"\bRENAME\s+COLUMN\b", RegexOptions.IgnoreCase);
    }
}
