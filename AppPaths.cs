using System.IO;

namespace SeraphicSense;

public static class AppPaths
{
    public const string AppFolderName = "SeraphicSense";

    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string ConfigPath => Path.Combine(AppDataDirectory, "config.json");
    public static string BackupPaksDirectory => Path.Combine(AppDataDirectory, "BackupPaks");
    public static string BundledBackupDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    public static void EnsureAppDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(BackupPaksDirectory);
    }

    public static void SeedBackupFolderFromBundledData()
    {
        EnsureAppDirectories();

        if (!Directory.Exists(BundledBackupDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(BundledBackupDirectory))
        {
            var destinationPath = Path.Combine(BackupPaksDirectory, Path.GetFileName(sourcePath));
            if (!File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath);
            }
        }
    }
}
