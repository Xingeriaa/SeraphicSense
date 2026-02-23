using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace SeraphicSense;

public sealed class GitHubUpdateService
{
    private static readonly HttpClient SharedHttpClient = CreateSharedClient();
    private readonly HttpClient _httpClient;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SeraphicSenseUpdater/1.0");
        return client;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(string repository, CancellationToken cancellationToken = default)
    {
        var normalizedRepository = NormalizeRepository(repository);
        if (string.IsNullOrWhiteSpace(normalizedRepository))
        {
            return UpdateCheckResult.Failed("GitHub repository must be owner/repo or a GitHub repository URL.");
        }

        var currentVersion = GetCurrentVersion();
        var endpoint = $"https://api.github.com/repos/{normalizedRepository}/releases/latest";

        using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await BuildResultFromReleaseResponseAsync(
                response,
                normalizedRepository,
                currentVersion,
                cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var fallbackTag = await TryGetLatestTagAsync(normalizedRepository, cancellationToken);
            if (string.IsNullOrWhiteSpace(fallbackTag))
            {
                return UpdateCheckResult.NoPublishedVersions(currentVersion);
            }

            var isUpdateAvailableFromTag = IsNewerThanCurrent(fallbackTag, currentVersion);
            return new UpdateCheckResult(
                IsSuccess: true,
                IsUpdateAvailable: isUpdateAvailableFromTag,
                CurrentVersion: currentVersion,
                LatestVersion: fallbackTag,
                ReleaseUrl: $"https://github.com/{normalizedRepository}/tags",
                ErrorMessage: string.Empty);
        }

        var reason = $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        return UpdateCheckResult.Failed(reason);
    }

    private static async Task<UpdateCheckResult> BuildResultFromReleaseResponseAsync(
        HttpResponseMessage response,
        string normalizedRepository,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var latestTag = root.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? string.Empty
            : string.Empty;
        var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(latestTag))
        {
            return UpdateCheckResult.Failed("Latest release has no tag_name.");
        }

        var isUpdateAvailable = IsNewerThanCurrent(latestTag, currentVersion);
        return new UpdateCheckResult(
            IsSuccess: true,
            IsUpdateAvailable: isUpdateAvailable,
            CurrentVersion: currentVersion,
            LatestVersion: latestTag,
            ReleaseUrl: releaseUrl,
            ErrorMessage: string.Empty);
    }

    private async Task<string> TryGetLatestTagAsync(string normalizedRepository, CancellationToken cancellationToken)
    {
        var tagsEndpoint = $"https://api.github.com/repos/{normalizedRepository}/tags?per_page=1";
        using var tagsResponse = await _httpClient.GetAsync(tagsEndpoint, cancellationToken);
        if (!tagsResponse.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        using var tagsStream = await tagsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var tagsDocument = await JsonDocument.ParseAsync(tagsStream, cancellationToken: cancellationToken);
        if (tagsDocument.RootElement.ValueKind != JsonValueKind.Array || tagsDocument.RootElement.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var firstTag = tagsDocument.RootElement[0];
        return firstTag.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeRepository(string? repositoryInput)
    {
        var input = (repositoryInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var path = uri.AbsolutePath.Trim('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^4];
            }

            var uriParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return uriParts.Length >= 2
                ? $"{uriParts[0]}/{uriParts[1]}"
                : string.Empty;
        }

        if (input.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            input = input[..^4];
        }

        var parts = input.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part))
            ? $"{parts[0]}/{parts[1]}"
            : string.Empty;
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static bool IsNewerThanCurrent(string latestTag, string currentVersion)
    {
        var normalizedLatest = NormalizeVersionText(latestTag);
        var normalizedCurrent = NormalizeVersionText(currentVersion);

        var latestParsed = Version.TryParse(normalizedLatest, out var latestVersion);
        var currentParsed = Version.TryParse(normalizedCurrent, out var current);

        if (latestParsed && currentParsed)
        {
            return latestVersion! > current!;
        }

        return !string.Equals(latestTag, currentVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersionText(string rawVersion)
    {
        var trimmed = rawVersion.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var separatorIndex = trimmed.IndexOfAny(['-', '+']);
        return separatorIndex > 0 ? trimmed[..separatorIndex] : trimmed;
    }
}

public readonly record struct UpdateCheckResult(
    bool IsSuccess,
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string ErrorMessage)
{
    public static UpdateCheckResult Failed(string message) =>
        new(
            IsSuccess: false,
            IsUpdateAvailable: false,
            CurrentVersion: string.Empty,
            LatestVersion: string.Empty,
            ReleaseUrl: string.Empty,
            ErrorMessage: message);

    public static UpdateCheckResult NoPublishedVersions(string currentVersion) =>
        new(
            IsSuccess: true,
            IsUpdateAvailable: false,
            CurrentVersion: currentVersion,
            LatestVersion: currentVersion,
            ReleaseUrl: string.Empty,
            ErrorMessage: "No GitHub releases or tags published yet.");
}
