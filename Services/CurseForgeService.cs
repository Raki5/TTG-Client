using System.Net.Http;
using System.Globalization;
using System.Net;
using System.Text.Json;
using TTGClient.Models;

namespace TTGClient.Services;

public sealed class CurseForgeService
{
    private const string ApiBase = "https://api.curseforge.com/v1";
    private static readonly HttpClient Client = CreateClient();
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _categoryLock = new(1, 1);
    private IReadOnlyList<CurseForgeCategory>? _cachedCategories;

    public CurseForgeService(SettingsService settings) => _settings = settings;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_settings.GetCurseForgeApiKey());

    public async Task<SearchPage> SearchAsync(SearchFilters filters, CancellationToken cancellationToken = default)
    {
        var key = _settings.GetCurseForgeApiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("CurseForge requires an API key. Open Settings to add your key; Modrinth search works without one.");

        var parameters = new List<string>
        {
            "gameId=432",
            "classId=6",
            $"pageSize={filters.PageSize.ToString(CultureInfo.InvariantCulture)}",
            $"index={((filters.Page - 1) * filters.PageSize).ToString(CultureInfo.InvariantCulture)}",
            $"sortField={GetSortField(filters.Sort)}",
            "sortOrder=desc"
        };
        if (!string.IsNullOrWhiteSpace(filters.Query)) parameters.Add($"searchFilter={Uri.EscapeDataString(filters.Query.Trim())}");
        if (!IsAny(filters.Version)) parameters.Add($"gameVersion={Uri.EscapeDataString(filters.Version)}");
        if (!IsAny(filters.Loader) && !IsAny(filters.Version)) parameters.Add($"modLoaderType={GetLoaderId(filters.Loader)}");

        if (!IsAny(filters.Category))
        {
            var categories = await GetCategoriesAsync(key, cancellationToken);
            var selected = FindCategory(categories, ModrinthService.ToCategorySlug(filters.Category), filters.Category);
            if (selected is null)
                throw new InvalidOperationException($"CurseForge doesn't expose a matching category for '{filters.Category}'. This search was stopped rather than returning unfiltered results.");
            parameters.Add($"categoryId={selected.Id.ToString(CultureInfo.InvariantCulture)}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/mods/search?{string.Join("&", parameters)}");
        request.Headers.Add("x-api-key", key);
        using var response = await Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw await ModrinthService.CreateApiException(response, "CurseForge");
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        var data = TryGet(root, "data");
        var projects = new List<ModProject>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var slug = GetString(item, "slug");
                var id = GetInt64(item, "id").ToString(CultureInfo.InvariantCulture);
                var authors = TryGet(item, "authors");
                var author = authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0 ? GetString(authors[0], "name", "Unknown author") : "Unknown author";
                var logo = TryGet(item, "logo");
                var links = TryGet(item, "links");
                var projectUrl = GetString(links, "websiteUrl", $"https://www.curseforge.com/minecraft/mc-mods/{slug}");
                var categories = TryGet(item, "categories");
                var categoryNames = categories.ValueKind == JsonValueKind.Array
                    ? categories.EnumerateArray().Select(c => GetString(c, "name")).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                    : Array.Empty<string>();
                var versionData = TryGet(item, "latestFilesIndexes");
                var supportedVersions = versionData.ValueKind == JsonValueKind.Array
                    ? versionData.EnumerateArray().Select(f => GetString(f, "gameVersion")).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : Array.Empty<string>();
                var loaders = versionData.ValueKind == JsonValueKind.Array
                    ? versionData.EnumerateArray().Select(f => LoaderName(GetInt64(f, "modLoader"))).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : Array.Empty<string>();

                projects.Add(new ModProject
                {
                    Id = id,
                    Slug = slug,
                    Title = GetString(item, "name", "Untitled project"),
                    Description = GetString(item, "summary"),
                    Author = author,
                    IconUrl = GetString(logo, "url"),
                    ProjectUrl = projectUrl,
                    Source = ProjectSource.CurseForge,
                    Downloads = GetInt64(item, "downloadCount"),
                    Followers = 0,
                    ModifiedAt = DateTimeOffset.TryParse(GetString(item, "dateModified"), out var modified) ? modified : null,
                    SupportedVersions = supportedVersions,
                    Categories = categoryNames,
                    Loaders = loaders
                });
            }
        }
        // The CurseForge REST search API has no equivalent client/server environment filter.
        // Do not silently pretend that filter was applied.
        var pagination = TryGet(root, "pagination");
        var total = GetInt64(pagination, "totalCount");
        var pageSize = GetInt64(pagination, "pageSize");
        var index = GetInt64(pagination, "index");
        return new SearchPage
        {
            Projects = projects,
            TotalCount = total,
            HasNext = pageSize > 0 && index + pageSize < total
        };
    }

    private async Task<IReadOnlyList<CurseForgeCategory>> GetCategoriesAsync(string key, CancellationToken cancellationToken)
    {
        if (_cachedCategories is not null) return _cachedCategories;
        await _categoryLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedCategories is not null) return _cachedCategories;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/categories?gameId=432&classId=6");
            request.Headers.Add("x-api-key", key);
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await ModrinthService.CreateApiException(response, "CurseForge categories");
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var data = TryGet(doc.RootElement, "data");
            _cachedCategories = data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray().Select(c => new CurseForgeCategory(
                    GetInt64(c, "id"),
                    GetString(c, "name"),
                    GetString(c, "slug"),
                    GetInt64(c, "parentCategoryId")))
                    .Where(c => c.Id > 0)
                    .ToArray()
                : Array.Empty<CurseForgeCategory>();
            return _cachedCategories;
        }
        finally
        {
            _categoryLock.Release();
        }
    }

    private static CurseForgeCategory? FindCategory(IReadOnlyList<CurseForgeCategory> categories, string slug, string label)
    {
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var slugKey = Normalize(slug);
        var labelKey = Normalize(label);
        return categories.FirstOrDefault(c => Normalize(c.Slug) == slugKey || Normalize(c.Name) == labelKey)
            ?? categories.FirstOrDefault(c => Normalize(c.Name).Contains(labelKey, StringComparison.Ordinal));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TTGClient/0.1.0 (Windows; Mod Browser)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static bool IsAny(string value) => string.IsNullOrWhiteSpace(value) || value == "Any" || value == "All categories" || value == "All environments";
    private static int GetLoaderId(string loader) => loader switch { "Forge" => 1, "Fabric" => 4, "Quilt" => 5, "NeoForge" => 6, _ => 0 };
    private static int GetSortField(string sort) => sort switch { "Downloads" => 6, "Recently Updated" => 3, "Name" => 4, _ => 2 };
    private static string LoaderName(long id) => id switch { 1 => "Forge", 4 => "Fabric", 5 => "Quilt", 6 => "NeoForge", _ => string.Empty };
    private static JsonElement TryGet(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static string GetString(JsonElement element, string name, string fallback = "") => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static long GetInt64(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var n) ? n : 0;
    private sealed record CurseForgeCategory(long Id, string Name, string Slug, long ParentId);
}
