namespace TTGClient.Models;

public enum ProjectSource
{
    Modrinth,
    CurseForge
}

public sealed class ModProject
{
    public string Id { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = "Untitled project";
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = "Unknown author";
    public string IconUrl { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
    public ProjectSource Source { get; init; }
    public long Downloads { get; init; }
    public long Followers { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public IReadOnlyList<string> SupportedVersions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();

    public string SourceName => Source == ProjectSource.Modrinth ? "Modrinth" : "CurseForge";
    public string FormattedDownloads => Downloads.ToString("N0");
    public string LastUpdatedText => ModifiedAt is null ? "Update date unavailable" : $"Updated {ModifiedAt.Value.LocalDateTime:dd MMM yyyy}";
    public string SupportedVersionsText => SupportedVersions.Count == 0 ? "Version details unavailable" : string.Join(", ", SupportedVersions.Take(5));
    public string TagsText
    {
        get
        {
            var tags = new List<string>();
            tags.AddRange(Loaders.Take(2).Select(NormalizeTag));
            tags.AddRange(Categories.Take(Math.Max(0, 3 - tags.Count)).Select(NormalizeTag));
            return tags.Count == 0 ? SourceName : string.Join("  ·  ", tags);
        }
    }

    private static string NormalizeTag(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].Replace('-', ' ');
}

public sealed class SearchPage
{
    public IReadOnlyList<ModProject> Projects { get; init; } = Array.Empty<ModProject>();
    public long TotalCount { get; init; }
    public bool HasNext { get; init; }
}

public sealed record SearchFilters(
    string Query,
    string Version,
    string Loader,
    string Category,
    string Environment,
    string Sort,
    int Page,
    int PageSize = 20);
