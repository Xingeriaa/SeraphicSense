using System.IO;
using System.Net;
using System.Net.Http;
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
            return await BuildResultFromReleaseResponseAsync(response, currentVersion, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var fallbackTag = await TryGetLatestTagAsync(normalizedRepository, cancellationToken);
            if (string.IsNullOrWhiteSpace(fallbackTag))
            {
                return UpdateCheckResult.NoPublishedVersions(currentVersion);
            }

            var isUpdateAvailableFromTag = IsNewerThanCurrent(fallbackTag, currentVersion, out _);
            return new UpdateCheckResult(
                IsSuccess: true,
                IsUpdateAvailable: isUpdateAvailableFromTag,
                UpdateKind: isUpdateAvailableFromTag ? UpdateKind.Application : UpdateKind.None,
                CurrentVersion: currentVersion,
                LatestVersion: fallbackTag,
                ReleaseUrl: $"https://github.com/{normalizedRepository}/tags",
                InstallerDownloadUrl: string.Empty,
                InstallerFileName: string.Empty,
                DataAssets: Array.Empty<UpdateDataAsset>(),
                ErrorMessage: string.Empty);
        }

        var reason = $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        return UpdateCheckResult.Failed(reason);
    }

    private static async Task<UpdateCheckResult> BuildResultFromReleaseResponseAsync(
        HttpResponseMessage response,
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
        var releaseBody = root.TryGetProperty("body", out var bodyElement)
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(latestTag))
        {
            return UpdateCheckResult.Failed("Latest release has no tag_name.");
        }

        var releaseAssets = ParseAssets(root);
        var installerAsset = FindInstallerAsset(releaseAssets);
        var dataAssets = FindDataAssets(releaseAssets);

        var isVersionNew = IsNewerThanCurrent(latestTag, currentVersion, out var comparableVersions);
        var hasInstaller = !string.IsNullOrWhiteSpace(installerAsset.DownloadUrl);
        var hasDataAssets = dataAssets.Count > 0;

        var updateKind = DetermineUpdateKind(
            isVersionNew,
            comparableVersions,
            hasInstaller,
            hasDataAssets,
            releaseBody);

        return new UpdateCheckResult(
            IsSuccess: true,
            IsUpdateAvailable: updateKind != UpdateKind.None,
            UpdateKind: updateKind,
            CurrentVersion: currentVersion,
            LatestVersion: latestTag,
            ReleaseUrl: releaseUrl,
            InstallerDownloadUrl: installerAsset.DownloadUrl,
            InstallerFileName: installerAsset.FileName,
            DataAssets: dataAssets,
            ErrorMessage: string.Empty);
    }

    private static UpdateKind DetermineUpdateKind(
        bool isVersionNew,
        bool comparableVersions,
        bool hasInstaller,
        bool hasDataAssets,
        string releaseBody)
    {
        var explicitKind = ParseExplicitUpdateKind(releaseBody);
        if (explicitKind == UpdateKind.DataOnly && hasDataAssets)
        {
            return UpdateKind.DataOnly;
        }

        if (explicitKind == UpdateKind.Application && (hasInstaller || isVersionNew || !comparableVersions))
        {
            return UpdateKind.Application;
        }

        if (hasInstaller && (isVersionNew || !comparableVersions))
        {
            return UpdateKind.Application;
        }

        if (hasDataAssets)
        {
            return UpdateKind.DataOnly;
        }

        if (isVersionNew)
        {
            return UpdateKind.Application;
        }

        return UpdateKind.None;
    }

    private static UpdateKind ParseExplicitUpdateKind(string releaseBody)
    {
        var body = (releaseBody ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(body))
        {
            return UpdateKind.None;
        }

        if (body.Contains("[update-type:data]") || body.Contains("update-type: data"))
        {
            return UpdateKind.DataOnly;
        }

        if (body.Contains("[update-type:app]") || body.Contains("update-type: app"))
        {
            return UpdateKind.Application;
        }

        return UpdateKind.None;
    }

    private static IReadOnlyList<UpdateDataAsset> ParseAssets(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<UpdateDataAsset>();
        }

        var parsed = new List<UpdateDataAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            parsed.Add(new UpdateDataAsset(name, url));
        }

        return parsed;
    }

    private static UpdateDataAsset FindInstallerAsset(IReadOnlyList<UpdateDataAsset> assets)
    {
        var bestScore = int.MaxValue;
        var best = default(UpdateDataAsset);

        foreach (var asset in assets)
        {
            var ext = Path.GetExtension(asset.FileName).ToLowerInvariant();
            if (ext is not ".exe" and not ".msi")
            {
                continue;
            }

            var score = ComputeInstallerScore(asset.FileName, ext);
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            best = asset;
        }

        return best;
    }

    private static IReadOnlyList<UpdateDataAsset> FindDataAssets(IReadOnlyList<UpdateDataAsset> assets)
    {
        var archiveCandidate = assets
            .Where(asset => IsDataArchiveAsset(asset.FileName))
            .OrderBy(asset => ComputeDataArchiveScore(asset.FileName))
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(archiveCandidate.DownloadUrl))
        {
            return [archiveCandidate];
        }

        return assets
            .Where(asset => IsDirectDataFileAsset(asset.FileName))
            .ToArray();
    }

    private static int ComputeInstallerScore(string fileName, string extension)
    {
        var normalized = fileName.ToLowerInvariant();
        var score = extension == ".exe" ? 10 : 20;

        if (normalized.Contains("setup") || normalized.Contains("installer"))
        {
            score -= 5;
        }

        if (normalized.Contains("portable") || normalized.Contains("debug"))
        {
            score += 10;
        }

        return score;
    }

    private static bool IsDataArchiveAsset(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        if (Path.GetExtension(normalized) != ".zip")
        {
            return false;
        }

        return normalized.Contains("data")
               || normalized.Contains("backup")
               || normalized.Contains("pak")
               || normalized.Contains("maturedata");
    }

    private static int ComputeDataArchiveScore(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        var score = 100;

        if (normalized.Contains("backuppaks"))
        {
            score -= 40;
        }

        if (normalized.Contains("maturedata"))
        {
            score -= 20;
        }

        if (normalized.Contains("data"))
        {
            score -= 10;
        }

        return score;
    }

    private static bool IsDirectDataFileAsset(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".pak" or ".sig" or ".ucas" or ".utoc";
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

    private static bool IsNewerThanCurrent(string latestTag, string currentVersion, out bool comparableVersions)
    {
        var latestParsed = TryParseComparableVersion(latestTag, out var latestVersion);
        var currentParsed = TryParseComparableVersion(currentVersion, out var currentVersionValue);
        comparableVersions = latestParsed && currentParsed;

        if (!comparableVersions)
        {
            return false;
        }

        return latestVersion > currentVersionValue;
    }

    private static bool TryParseComparableVersion(string rawVersion, out Version parsedVersion)
    {
        var normalized = NormalizeVersionText(rawVersion);
        var parsed = Version.TryParse(normalized, out var value);
        parsedVersion = value ?? new Version(0, 0);
        return parsed;
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

public enum UpdateKind
{
    None = 0,
    Application = 1,
    DataOnly = 2
}

public readonly record struct UpdateDataAsset(string FileName, string DownloadUrl);

public readonly record struct UpdateCheckResult(
    bool IsSuccess,
    bool IsUpdateAvailable,
    UpdateKind UpdateKind,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string InstallerDownloadUrl,
    string InstallerFileName,
    IReadOnlyList<UpdateDataAsset> DataAssets,
    string ErrorMessage)
{
    public static UpdateCheckResult Failed(string message) =>
        new(
            IsSuccess: false,
            IsUpdateAvailable: false,
            UpdateKind: UpdateKind.None,
            CurrentVersion: string.Empty,
            LatestVersion: string.Empty,
            ReleaseUrl: string.Empty,
            InstallerDownloadUrl: string.Empty,
            InstallerFileName: string.Empty,
            DataAssets: Array.Empty<UpdateDataAsset>(),
            ErrorMessage: message);

    public static UpdateCheckResult NoPublishedVersions(string currentVersion) =>
        new(
            IsSuccess: true,
            IsUpdateAvailable: false,
            UpdateKind: UpdateKind.None,
            CurrentVersion: currentVersion,
            LatestVersion: currentVersion,
            ReleaseUrl: string.Empty,
            InstallerDownloadUrl: string.Empty,
            InstallerFileName: string.Empty,
            DataAssets: Array.Empty<UpdateDataAsset>(),
            ErrorMessage: "No GitHub releases or tags published yet.");
}
