namespace SeraphicSense;

public sealed class GuardianConfig
{
    public string ObservedFolderPath { get; set; } = @"C:\Riot Games\VALORANT\live\ShooterGame\Content\Paks";
    public string SourceFolderPath { get; set; } = AppPaths.BackupPaksDirectory;
    public string RequiredBaseName { get; set; } = "MatureData-WindowsClient";
    public string[] RequiredExtensions { get; set; } = ["pak", "sig", "ucas", "utoc"];
    public string ForbiddenBaseName { get; set; } = "VNGLogo-WindowsClient";
    public int ValidationDelayMs { get; set; } = 2000;

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool AutoStartMonitoring { get; set; } = true;
    public bool CheckUpdatesOnLaunch { get; set; } = true;
    public string GitHubRepository { get; set; } = AppConstants.FixedGitHubRepositoryUrl;
    public string LastAppliedDataReleaseTag { get; set; } = string.Empty;
}
