using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ScraperTool.Data;

internal sealed class DatabaseMigrator(
    AppDbContext db,
    ILogger<DatabaseMigrator> logger) : IDatabaseMigrator
{
    public async Task MigrateAsync()
    {
        logger.LogInformation("--- Database migration: checking pending migrations...");

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation(
                "--- Database migration: no pending migrations, schema is up to date.");
        }
        else
        {
            logger.LogInformation(
                "--- Database migration: applying {Count} migration(s): {Migrations}",
                pending.Count,
                string.Join(", ", pending));

            await db.Database.MigrateAsync();

            logger.LogInformation("--- Database migration: completed successfully.");
        }
    }
}
