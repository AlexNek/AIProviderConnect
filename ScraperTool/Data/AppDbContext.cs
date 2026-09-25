using Microsoft.EntityFrameworkCore;

using ScraperTool.Data.Entities;

namespace ScraperTool.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<ModelEntry> Models => Set<ModelEntry>();

    public DbSet<ProviderEntry> Providers => Set<ProviderEntry>();

    public DbSet<TokenUsageEntry> TokenUsages => Set<TokenUsageEntry>();

    public DbSet<ValidationIssueEntry> ValidationIssues => Set<ValidationIssueEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderEntry>(e => { e.HasIndex(p => p.ProviderId).IsUnique(); });

        modelBuilder.Entity<ModelEntry>(e =>
            {
                e.HasIndex(m => new { m.ProviderEntryId, m.ModelId });
                e.HasOne(m => m.Provider)
                    .WithMany(p => p.Models)
                    .HasForeignKey(m => m.ProviderEntryId);
            });
    }
}
