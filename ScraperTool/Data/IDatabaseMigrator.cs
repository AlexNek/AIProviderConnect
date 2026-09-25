namespace ScraperTool.Data;

/// <summary>
/// Runs database migrations and seeds default data.
/// </summary>
public interface IDatabaseMigrator
{
    Task MigrateAsync();
}
