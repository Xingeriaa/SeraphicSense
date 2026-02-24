using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace SeraphicSense;

public sealed class DataUpdateService
{
    private static readonly HttpClient SharedHttpClient = CreateSharedClient();
    private readonly HttpClient _httpClient;

    public DataUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SeraphicSenseDataUpdater/1.0");
        return client;
    }

    public async Task<UpdateInstallResult> InstallDataUpdateAsync(
        UpdateCheckResult update,
        GuardianConfig config,
        CancellationToken cancellationToken = default)
    {
        if (update.UpdateKind != UpdateKind.DataOnly)
        {
            return UpdateInstallResult.Failed("Latest release is not a data-only update.");
        }

        if (update.DataAssets.Count == 0)
        {
            return UpdateInstallResult.Failed("No data assets were found in the release.");
        }

        try
        {
            Directory.CreateDirectory(config.SourceFolderPath);

            var updateDirectory = Path.Combine(
                Path.GetTempPath(),
                AppPaths.AppFolderName,
                "data-updates",
                SanitizeFolderName(update.LatestVersion));
            Directory.CreateDirectory(updateDirectory);

            var expectedFileNames = config.RequiredExtensions
                .Select(extension => $"{config.RequiredBaseName}.{extension.Trim().TrimStart('.')}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var updatedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in update.DataAssets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localPath = Path.Combine(updateDirectory, SanitizeFileName(asset.FileName));
                var downloaded = await DownloadAssetAsync(asset.DownloadUrl, localPath, cancellationToken);
                if (!downloaded.IsSuccess)
                {
                    return downloaded;
                }

                var extension = Path.GetExtension(localPath).ToLowerInvariant();
                if (extension == ".zip")
                {
                    var extractedCount = await ExtractMatchingFilesFromZipAsync(
                        localPath,
                        config.SourceFolderPath,
                        expectedFileNames,
                        updatedFiles,
                        cancellationToken);

                    if (extractedCount > 0)
                    {
                        continue;
                    }

                    // No exact matches found. Fall back to first matching extension for robustness.
                    extractedCount = await ExtractByKnownExtensionsAsync(
                        localPath,
                        config.SourceFolderPath,
                        config.RequiredExtensions,
                        updatedFiles,
                        cancellationToken);

                    if (extractedCount == 0)
                    {
                        return UpdateInstallResult.Failed($"Data archive did not contain expected files: {asset.FileName}");
                    }

                    continue;
                }

                var downloadedName = Path.GetFileName(localPath);
                if (!expectedFileNames.Contains(downloadedName))
                {
                    continue;
                }

                var destinationPath = Path.Combine(config.SourceFolderPath, downloadedName);
                File.Copy(localPath, destinationPath, overwrite: true);
                updatedFiles.Add(downloadedName);
            }

            if (updatedFiles.Count == 0)
            {
                return UpdateInstallResult.Failed("Release data assets were downloaded, but no expected data files were found.");
            }

            return UpdateInstallResult.Success(
                $"Data updated ({updatedFiles.Count} file{(updatedFiles.Count == 1 ? string.Empty : "s")}) from release {update.LatestVersion}.");
        }
        catch (Exception ex)
        {
            return UpdateInstallResult.Failed($"Data update failed: {ex.Message}");
        }
    }

    private async Task<UpdateInstallResult> DownloadAssetAsync(
        string downloadUrl,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return UpdateInstallResult.Failed(
                $"Failed to download data asset ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            81920,
            useAsync: true);
        await downloadStream.CopyToAsync(fileStream, cancellationToken);

        return UpdateInstallResult.Success(string.Empty);
    }

    private static async Task<int> ExtractMatchingFilesFromZipAsync(
        string zipPath,
        string destinationDirectory,
        HashSet<string> expectedFileNames,
        HashSet<string> updatedFiles,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var extracted = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (!expectedFileNames.Contains(entry.Name))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationDirectory, entry.Name);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                81920,
                useAsync: true);
            await entryStream.CopyToAsync(fileStream, cancellationToken);

            updatedFiles.Add(entry.Name);
            extracted++;
        }

        return extracted;
    }

    private static async Task<int> ExtractByKnownExtensionsAsync(
        string zipPath,
        string destinationDirectory,
        IReadOnlyCollection<string> requiredExtensions,
        HashSet<string> updatedFiles,
        CancellationToken cancellationToken)
    {
        var normalizedExtensions = requiredExtensions
            .Select(value => value.Trim().TrimStart('.'))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalizedExtensions.Count == 0)
        {
            return 0;
        }

        using var archive = ZipFile.OpenRead(zipPath);
        var extracted = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var entryExtension = Path.GetExtension(entry.Name).TrimStart('.');
            if (!normalizedExtensions.Contains(entryExtension))
            {
                continue;
            }

            var safeName = Path.GetFileName(entry.Name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationDirectory, safeName);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                81920,
                useAsync: true);
            await entryStream.CopyToAsync(fileStream, cancellationToken);

            updatedFiles.Add(safeName);
            extracted++;
        }

        return extracted;
    }

    private static string SanitizeFileName(string fileName)
    {
        var cleaned = fileName.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "data-update.zip" : cleaned;
    }

    private static string SanitizeFolderName(string value)
    {
        var cleaned = value.Trim();
        foreach (var invalidChar in Path.GetInvalidPathChars())
        {
            cleaned = cleaned.Replace(invalidChar, '_');
        }

        cleaned = cleaned.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "latest" : cleaned;
    }
}
