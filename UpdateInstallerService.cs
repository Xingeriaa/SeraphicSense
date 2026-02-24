using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace SeraphicSense;

public sealed class UpdateInstallerService
{
    private static readonly HttpClient SharedHttpClient = CreateSharedClient();
    private readonly HttpClient _httpClient;

    public UpdateInstallerService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SeraphicSenseUpdater/1.0");
        return client;
    }

    public async Task<UpdateInstallResult> InstallUpdateAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        if (!update.IsUpdateAvailable)
        {
            return UpdateInstallResult.Failed("No update available.");
        }

        if (update.UpdateKind != UpdateKind.Application)
        {
            return UpdateInstallResult.Failed("Latest release is not an application installer update.");
        }

        if (string.IsNullOrWhiteSpace(update.InstallerDownloadUrl))
        {
            return UpdateInstallResult.Failed("Update found, but no installer asset was attached to the release.");
        }

        try
        {
            var fileName = ResolveFileName(update);
            var updateDirectory = Path.Combine(
                Path.GetTempPath(),
                AppPaths.AppFolderName,
                "updates",
                SanitizeFolderName(update.LatestVersion));

            Directory.CreateDirectory(updateDirectory);
            var installerPath = Path.Combine(updateDirectory, fileName);

            using (var response = await _httpClient.GetAsync(
                       update.InstallerDownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return UpdateInstallResult.Failed(
                        $"Failed to download installer ({(int)response.StatusCode} {response.ReasonPhrase}).");
                }

                await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(
                    installerPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    81920,
                    useAsync: true);
                await downloadStream.CopyToAsync(fileStream, cancellationToken);
            }

            var launchStarted = LaunchInstaller(installerPath);
            if (!launchStarted)
            {
                return UpdateInstallResult.Failed("Downloaded update but failed to launch installer.");
            }

            return UpdateInstallResult.Success($"Installer launched: {Path.GetFileName(installerPath)}");
        }
        catch (Exception ex)
        {
            return UpdateInstallResult.Failed($"Auto-update failed: {ex.Message}");
        }
    }

    private static string ResolveFileName(UpdateCheckResult update)
    {
        if (!string.IsNullOrWhiteSpace(update.InstallerFileName))
        {
            return SanitizeFileName(update.InstallerFileName);
        }

        if (Uri.TryCreate(update.InstallerDownloadUrl, UriKind.Absolute, out var uri))
        {
            var uriName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(uriName))
            {
                return SanitizeFileName(uriName);
            }
        }

        return $"SeraphicSense-Setup-{SanitizeFolderName(update.LatestVersion)}.exe";
    }

    private static bool LaunchInstaller(string installerPath)
    {
        var extension = Path.GetExtension(installerPath).ToLowerInvariant();
        ProcessStartInfo startInfo;

        if (extension == ".msi")
        {
            startInfo = new ProcessStartInfo("msiexec.exe")
            {
                Arguments = $"/i \"{installerPath}\" /qn /norestart",
                UseShellExecute = true
            };
        }
        else
        {
            startInfo = new ProcessStartInfo(installerPath)
            {
                // Inno Setup understands these switches.
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS",
                UseShellExecute = true
            };
        }

        return Process.Start(startInfo) is not null;
    }

    private static string SanitizeFileName(string fileName)
    {
        var cleaned = fileName.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "update-installer.exe" : cleaned;
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

public readonly record struct UpdateInstallResult(bool IsSuccess, string Message)
{
    public static UpdateInstallResult Success(string message) => new(true, message);
    public static UpdateInstallResult Failed(string message) => new(false, message);
}
