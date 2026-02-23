using System.Diagnostics;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SeraphicSense;

public partial class MainWindow : Window
{
    private readonly ConfigStore _configStore;
    private readonly FolderGuardianService _guardianService;
    private readonly StartupManager _startupManager;
    private readonly GitHubUpdateService _updateService;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _trayStartStopItem;
    private readonly string _startupValueName = AppPaths.AppFolderName;

    private GuardianConfig _config;
    private bool _exitRequested;
    private bool _didInitialTrayHint;

    public MainWindow()
    {
        InitializeComponent();

        _configStore = new ConfigStore();
        _guardianService = new FolderGuardianService();
        _startupManager = new StartupManager();
        _updateService = new GitHubUpdateService();

        _guardianService.StatusChanged += OnStatusChanged;

        _config = _configStore.Load();
        _config.StartWithWindows = _startupManager.IsEnabled(_startupValueName);
        ApplyConfigToInputs(_config);

        _trayStartStopItem = new Forms.ToolStripMenuItem("Start Monitoring");
        _trayIcon = BuildTrayIcon();

        Loaded += OnWindowLoaded;
        StateChanged += OnWindowStateChanged;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;

        SetStatus($"Idle. Config file: {_configStore.ConfigPath}");
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_config.AutoStartMonitoring)
        {
            StartMonitoring();
        }

        if (_config.CheckUpdatesOnLaunch && !string.IsNullOrWhiteSpace(_config.GitHubRepository))
        {
            await CheckForUpdatesAsync(manualCheck: false);
        }

        if (_config.StartMinimized || Environment.GetCommandLineArgs().Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            HideToTray(showHint: false);
        }
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var contextMenu = new Forms.ContextMenuStrip();

        var openItem = new Forms.ToolStripMenuItem("Open");
        var checkUpdatesItem = new Forms.ToolStripMenuItem("Check Updates");
        var exitItem = new Forms.ToolStripMenuItem("Exit");

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(_trayStartStopItem);
        contextMenu.Items.Add(checkUpdatesItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "SeraphicSense",
            ContextMenuStrip = contextMenu
        };

        openItem.Click += (_, _) => OpenFromTray();
        _trayStartStopItem.Click += (_, _) => ToggleMonitoringFromTray();
        checkUpdatesItem.Click += (_, _) => CheckForUpdatesFromTray();
        exitItem.Click += (_, _) => ExitFromTray();
        icon.DoubleClick += (_, _) => OpenFromTray();

        return icon;
    }

    private static void OpenReleaseUrl(string releaseUrl)
    {
        if (!Uri.IsWellFormedUriString(releaseUrl, UriKind.Absolute))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(manualCheck: true);
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromInputs();
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_guardianService.IsRunning)
        {
            StopMonitoring();
            return;
        }

        StartMonitoring();
    }

    private void ObservedBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = SelectFolder(ObservedFolderTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            ObservedFolderTextBox.Text = selectedPath;
        }
    }

    private void SourceBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = SelectFolder(SourceFolderTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SourceFolderTextBox.Text = selectedPath;
        }
    }

    private static string? SelectFolder(string currentPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select folder",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        if (Directory.Exists(currentPath))
        {
            dialog.SelectedPath = currentPath;
        }

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private GuardianConfig BuildConfigFromInputs()
    {
        if (string.IsNullOrWhiteSpace(ObservedFolderTextBox.Text))
        {
            throw new InvalidOperationException("Observed Folder Path is required.");
        }

        if (string.IsNullOrWhiteSpace(SourceFolderTextBox.Text))
        {
            throw new InvalidOperationException("Source Folder Path is required.");
        }

        if (string.IsNullOrWhiteSpace(RequiredBaseNameTextBox.Text))
        {
            throw new InvalidOperationException("Required Base Name is required.");
        }

        if (string.IsNullOrWhiteSpace(ForbiddenBaseNameTextBox.Text))
        {
            throw new InvalidOperationException("Forbidden Base Name is required.");
        }

        var delayText = ValidationDelayTextBox.Text.Trim();
        var hasDelay = !string.IsNullOrWhiteSpace(delayText);
        var validationDelayMs = 2000;

        if (hasDelay && !int.TryParse(delayText, out validationDelayMs))
        {
            throw new InvalidOperationException("Validation Delay (ms) must be a whole number.");
        }

        if (validationDelayMs <= 0)
        {
            validationDelayMs = 2000;
        }

        validationDelayMs = Math.Clamp(validationDelayMs, 1, 60_000);

        var requiredExtensions = RequiredExtensionsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.Trim().TrimStart('.'))
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requiredExtensions.Length == 0)
        {
            throw new InvalidOperationException("Required Extensions must include at least one value.");
        }

        return new GuardianConfig
        {
            ObservedFolderPath = ObservedFolderTextBox.Text.Trim(),
            SourceFolderPath = SourceFolderTextBox.Text.Trim(),
            RequiredBaseName = RequiredBaseNameTextBox.Text.Trim(),
            RequiredExtensions = requiredExtensions,
            ForbiddenBaseName = ForbiddenBaseNameTextBox.Text.Trim(),
            ValidationDelayMs = validationDelayMs,
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            StartMinimized = StartMinimizedCheckBox.IsChecked == true,
            AutoStartMonitoring = AutoStartMonitoringCheckBox.IsChecked == true,
            CheckUpdatesOnLaunch = CheckUpdatesOnLaunchCheckBox.IsChecked == true,
            GitHubRepository = GitHubRepositoryTextBox.Text.Trim()
        };
    }

    private void ApplyConfigToInputs(GuardianConfig config)
    {
        ObservedFolderTextBox.Text = config.ObservedFolderPath;
        SourceFolderTextBox.Text = config.SourceFolderPath;
        RequiredBaseNameTextBox.Text = config.RequiredBaseName;
        RequiredExtensionsTextBox.Text = string.Join(", ", config.RequiredExtensions);
        ForbiddenBaseNameTextBox.Text = config.ForbiddenBaseName;
        ValidationDelayTextBox.Text = config.ValidationDelayMs.ToString();
        GitHubRepositoryTextBox.Text = config.GitHubRepository;
        StartWithWindowsCheckBox.IsChecked = config.StartWithWindows;
        StartMinimizedCheckBox.IsChecked = config.StartMinimized;
        AutoStartMonitoringCheckBox.IsChecked = config.AutoStartMonitoring;
        CheckUpdatesOnLaunchCheckBox.IsChecked = config.CheckUpdatesOnLaunch;
        ToggleInputState(_guardianService.IsRunning);
    }

    private bool SaveSettingsFromInputs()
    {
        try
        {
            _config = BuildConfigFromInputs();
            _configStore.Save(_config);
            _startupManager.SetEnabled(_startupValueName, _config.StartWithWindows);
            SetStatus("Settings saved.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to save settings: {ex.Message}");
            return false;
        }
    }

    private void StartMonitoring()
    {
        try
        {
            if (!SaveSettingsFromInputs())
            {
                return;
            }

            _guardianService.Start(_config);
            ToggleInputState(isRunning: true);
            _ = _guardianService.ValidateAndHealAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to start monitoring: {ex.Message}");
        }
    }

    private void StopMonitoring()
    {
        _guardianService.Stop();
        ToggleInputState(isRunning: false);
    }

    private void ToggleInputState(bool isRunning)
    {
        ObservedFolderTextBox.IsEnabled = !isRunning;
        SourceFolderTextBox.IsEnabled = !isRunning;
        RequiredBaseNameTextBox.IsEnabled = !isRunning;
        RequiredExtensionsTextBox.IsEnabled = !isRunning;
        ForbiddenBaseNameTextBox.IsEnabled = !isRunning;
        ValidationDelayTextBox.IsEnabled = !isRunning;
        ObservedBrowseButton.IsEnabled = !isRunning;
        SourceBrowseButton.IsEnabled = !isRunning;
        StartStopButton.Content = isRunning ? "Stop Monitoring" : "Start Monitoring";
        _trayStartStopItem.Text = isRunning ? "Stop Monitoring" : "Start Monitoring";
    }

    private async Task CheckForUpdatesAsync(bool manualCheck)
    {
        if (!SaveSettingsFromInputs())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.GitHubRepository))
        {
            if (manualCheck)
            {
                SetStatus("Set GitHub Repository first (owner/repo).");
            }

            return;
        }

        try
        {
            SetStatus("Checking GitHub releases...");
            var result = await _updateService.CheckForUpdateAsync(_config.GitHubRepository);

            if (!result.IsSuccess)
            {
                SetStatus($"Update check failed: {result.ErrorMessage}");
                return;
            }

            if (!result.IsUpdateAvailable)
            {
                if (manualCheck)
                {
                    SetStatus($"Up to date. Current {result.CurrentVersion}, latest {result.LatestVersion}.");
                }

                return;
            }

            SetStatus($"Update available: {result.LatestVersion} (current {result.CurrentVersion}).");
            _trayIcon.BalloonTipTitle = "SeraphicSense Update";
            _trayIcon.BalloonTipText = $"New version {result.LatestVersion} available.";
            _trayIcon.ShowBalloonTip(3000);

            var shouldOpen = manualCheck || IsVisible;
            if (shouldOpen && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
            {
                var response = System.Windows.MessageBox.Show(
                    $"Version {result.LatestVersion} is available. Open release page?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (response == MessageBoxResult.Yes)
                {
                    OpenReleaseUrl(result.ReleaseUrl);
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Update check error: {ex.Message}");
        }
    }

    private void OnStatusChanged(string message)
    {
        _ = Dispatcher.InvokeAsync(() => SetStatus(message));
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message.StartsWith('[')
            ? message
            : $"[{DateTime.Now:HH:mm:ss}] {message}";
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !_exitRequested)
        {
            HideToTray(showHint: false);
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray(showHint: true);
    }

    private void HideToTray(bool showHint)
    {
        ShowInTaskbar = false;
        Hide();
        WindowState = WindowState.Normal;

        if (!showHint || _didInitialTrayHint)
        {
            return;
        }

        _didInitialTrayHint = true;
        _trayIcon.BalloonTipTitle = "SeraphicSense";
        _trayIcon.BalloonTipText = "Still running in tray. Use tray menu to open or exit.";
        _trayIcon.ShowBalloonTip(2500);
    }

    private void OpenFromTray()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            Show();
            ShowInTaskbar = true;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }

    private void ToggleMonitoringFromTray()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_guardianService.IsRunning)
            {
                StopMonitoring();
            }
            else
            {
                StartMonitoring();
            }
        });
    }

    private void CheckForUpdatesFromTray()
    {
        _ = Dispatcher.InvokeAsync(async () => await CheckForUpdatesAsync(manualCheck: true));
    }

    private void ExitFromTray()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _exitRequested = true;
            Close();
        });
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _guardianService.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
