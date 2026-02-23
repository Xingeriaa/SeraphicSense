using System.IO;
using System.Text.Json;

namespace SeraphicSense;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;

    public ConfigStore(string? configPath = null)
    {
        _configPath = configPath ?? AppPaths.ConfigPath;
    }

    public string ConfigPath => _configPath;

    public GuardianConfig Load()
    {
        AppPaths.EnsureAppDirectories();
        AppPaths.SeedBackupFolderFromBundledData();

        if (!File.Exists(_configPath))
        {
            var defaultConfig = new GuardianConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var loadedConfig = JsonSerializer.Deserialize<GuardianConfig>(json) ?? new GuardianConfig();
            Normalize(loadedConfig);
            return loadedConfig;
        }
        catch
        {
            var fallbackConfig = new GuardianConfig();
            Save(fallbackConfig);
            return fallbackConfig;
        }
    }

    public void Save(GuardianConfig config)
    {
        Normalize(config);

        var configDirectory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            Directory.CreateDirectory(configDirectory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private static void Normalize(GuardianConfig config)
    {
        config.ObservedFolderPath = NormalizeOrDefault(
            config.ObservedFolderPath,
            @"C:\Riot Games\VALORANT\live\ShooterGame\Content\Paks");

        config.SourceFolderPath = NormalizeOrDefault(
            config.SourceFolderPath,
            AppPaths.BackupPaksDirectory);

        config.RequiredBaseName = NormalizeOrDefault(
            config.RequiredBaseName,
            "MatureData-WindowsClient");

        config.ForbiddenBaseName = NormalizeOrDefault(
            config.ForbiddenBaseName,
            "VNGLogo-WindowsClient");

        config.ValidationDelayMs = config.ValidationDelayMs <= 0 ? 2000 : Math.Clamp(config.ValidationDelayMs, 1, 60_000);

        config.GitHubRepository = AppConstants.FixedGitHubRepositoryUrl;

        config.RequiredExtensions = (config.RequiredExtensions ?? [])
            .Select(extension => extension.Trim().TrimStart('.'))
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (config.RequiredExtensions.Length == 0)
        {
            config.RequiredExtensions = ["pak", "sig", "ucas", "utoc"];
        }
    }

    private static string NormalizeOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
