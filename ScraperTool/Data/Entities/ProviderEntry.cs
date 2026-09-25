namespace ScraperTool.Data.Entities;

public sealed class ProviderEntry
{
    public required string DisplayName { get; set; }

    public int Id { get; set; }

    public DateTime LastFetchedAt { get; set; }

    public ICollection<ModelEntry> Models { get; set; } = [];

    public required string ProviderId { get; set; }
}
