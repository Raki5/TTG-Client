using System.Net.Http;
using System.Globalization;
using System.Net;
using System.Text.Json;
using TTGClient.Models;

namespace TTGClient.Services;

public sealed class ModrinthService
{
    private static readonly HttpClient Client = CreateClient();
    private const string ApiBase = "https://api.modrinth.com/v2";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TTGClient/0.1.0 (Windows; Mod Browser)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public async Task<SearchPage> SearchAsync(SearchFilters filters, CancellationToken cancellationToken = default)
    {
        var facets = new List<string[]> { new[] { "project_type:mod" } };
        if (!IsAny(filters.Version)) facets.Add(new[] { $"versions:{filters.Version}" });
        if (!IsAny(filters.Loader)) facets.Add(new[] { $"categories:{ToModrinthLoader(filters.Loader)}" });
        if (!IsAny(filters.Category)) facets.Add(new[] { $"categories:{ToCategorySlug(filters.Category)}" });
        if (!IsAny(filters.Environment)) facets.Add(new[] { $"environment:{ToEnvironmentFacet(filters.Environment)}" });

        var query = new List<string>
        {
            $"limit={filters.PageSize.ToString(CultureInfo.InvariantCulture)}",
            $"offset={((filters.Page - 1) * filters.PageSize).ToString(CultureInfo.InvariantCulture)}",
            $"index={Uri.EscapeDataString(ToModrinthSort(filters.Sort))}",
            $"facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}"
        };
        if (!string.IsNullOrWhiteSpace(filters.Query)) query.Add($"query={Uri.EscapeDataString(filters.Query.Trim())}");

        using var response = await Client.GetAsync($"{ApiBase}/search?{string.Join("&", query)}", cancellationToken);
        if (!response.IsSuccessStatusCode) throw await CreateApiException(response, "Modrinth");
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = doc.RootElement;
        var projects = new List<ModProject>();
        if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                var slug = GetString(hit, "slug");
                var projectId = GetString(hit, "project_id");
                projects.Add(new ModProject
                {
                    Id = projectId,
                    Slug = slug,
                    Title = GetString(hit, "title", "Untitled project"),
                    Description = GetString(hit, "description"),
                    Author = GetString(hit, "author", "Unknown author"),
                    IconUrl = GetString(hit, "icon_url"),
                    ProjectUrl = $"https://modrinth.com/mod/{slug}",
                    Source = ProjectSource.Modrinth,
                    Downloads = GetInt64(hit, "downloads"),
                    Followers = GetInt64(hit, "follows"),
                    ModifiedAt = GetDateTime(hit, "date_modified"),
                    SupportedVersions = GetStringArray(hit, "versions"),
                    Categories = GetStringArray(hit, "categories").Where(c => !IsLoader(c)).ToArray(),
                    Loaders = GetStringArray(hit, "categories").Where(IsLoader).ToArray()
                });
            }
        }

        var total = GetInt64(root, "total_hits");
        return new SearchPage { Projects = projects, TotalCount = total, HasNext = filters.Page * filters.PageSize < total };
    }

    public static string ToCategorySlug(string value) => value switch
    {
        "Utility & QoL" => "utility",
        "World Generation" => "worldgen",
        "Food" => "food",
        "Storage" => "storage",
        "Decoration" => "decoration",
        "Adventure" => "adventure",
        "Technology" => "technology",
        "Magic" => "magic",
        "Optimization" => "optimization",
        "Mobs" => "mobs",
        "Transportation" => "transportation",
        _ => value.Trim().ToLowerInvariant().Replace(' ', '-')
    };

    private static string ToModrinthLoader(string value) => value.ToLowerInvariant() switch
    {
        "neoForge" => "neoforge",
        "neoforge" => "neoforge",
        _ => value.ToLowerInvariant()
    };

    private static string ToEnvironmentFacet(string value) => value switch
    {
        "Client only" => "client_only",
        "Server only" => "server_only",
        "Client + server" => "client_and_server",
        _ => value.ToLowerInvariant()
    };

    private static string ToModrinthSort(string sort) => sort switch
    {
        "Downloads" => "downloads",
        "Followers" => "follows",
        "Recently Updated" => "updated",
        "Newest" => "newest",
        _ => "relevance"
    };

    private static bool IsAny(string value) => string.IsNullOrWhiteSpace(value) || value == "Any" || value == "All categories" || value == "All environments";
    private static bool IsLoader(string value) => new[] { "forge", "fabric", "neoforge", "quilt", "liteloader" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static string GetString(JsonElement element, string name, string fallback = "") => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static long GetInt64(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var n) ? n : 0;
    private static DateTimeOffset? GetDateTime(JsonElement element, string name) => DateTimeOffset.TryParse(GetString(element, name), out var value) ? value : null;
    private static string[] GetStringArray(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray() : Array.Empty<string>();

    internal static async Task<Exception> CreateApiException(HttpResponseMessage response, string source)
    {
        var body = await response.Content.ReadAsStringAsync();
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return new InvalidOperationException($"{source} is rate limiting requests. Wait a moment and try again.");
        return new InvalidOperationException($"{source} API returned {(int)response.StatusCode} {response.ReasonPhrase}. {TrimError(detail)}");
    }

    internal static string TrimError(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length > 260 ? value[..260] + "…" : value;
}
