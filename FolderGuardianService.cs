using System.IO;
using System.Threading;

namespace SeraphicSense;

public sealed class FolderGuardianService : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _validationLock = new(1, 1);

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _validationTimer;
    private GuardianConfig? _config;

    public bool IsRunning { get; private set; }
    public event Action<string>? StatusChanged;

    public void Start(GuardianConfig config)
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ObservedFolderPath))
        {
            throw new InvalidOperationException("Observed folder path is required.");
        }

        if (string.IsNullOrWhiteSpace(config.SourceFolderPath))
        {
            throw new InvalidOperationException("Source folder path is required.");
        }

        if (string.IsNullOrWhiteSpace(config.RequiredBaseName))
        {
            throw new InvalidOperationException("Required base name is required.");
        }

        if (string.IsNullOrWhiteSpace(config.ForbiddenBaseName))
        {
            throw new InvalidOperationException("Forbidden base name is required.");
        }

        Directory.CreateDirectory(config.ObservedFolderPath);

        if (!Directory.Exists(config.SourceFolderPath))
        {
            throw new DirectoryNotFoundException($"Source folder does not exist: {config.SourceFolderPath}");
        }

        _config = CloneConfig(config);

        _watcher = new FileSystemWatcher(_config.ObservedFolderPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        _watcher.Created += OnFolderChanged;
        _watcher.Deleted += OnFolderChanged;
        _watcher.Renamed += OnFolderRenamed;
        _watcher.EnableRaisingEvents = true;

        _validationTimer = new System.Threading.Timer(OnValidationTimerTick, null, Timeout.Infinite, Timeout.Infinite);

        IsRunning = true;
        PublishStatus($"Monitoring started: {_config.ObservedFolderPath}");
        ScheduleValidation();
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        lock (_sync)
        {
            _validationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _validationTimer?.Dispose();
            _validationTimer = null;
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFolderChanged;
            _watcher.Deleted -= OnFolderChanged;
            _watcher.Renamed -= OnFolderRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        PublishStatus("Monitoring stopped.");
    }

    public async Task ValidateAndHealAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _config is null)
        {
            return;
        }

        await _validationLock.WaitAsync(cancellationToken);

        try
        {
            foreach (var extension in _config.RequiredExtensions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var requiredFileName = $"{_config.RequiredBaseName}.{extension}";
                var destinationPath = Path.Combine(_config.ObservedFolderPath, requiredFileName);

                if (File.Exists(destinationPath))
                {
                    continue;
                }

                var sourcePath = Path.Combine(_config.SourceFolderPath, requiredFileName);
                if (!File.Exists(sourcePath))
                {
                    PublishStatus($"Source missing: {requiredFileName}");
                    continue;
                }

                await CopyWithRetryAsync(sourcePath, destinationPath, cancellationToken);
                PublishStatus($"Restored {requiredFileName}");
            }

            var forbiddenPattern = $"{_config.ForbiddenBaseName}*";
            foreach (var forbiddenPath in Directory.EnumerateFiles(_config.ObservedFolderPath, forbiddenPattern))
            {
                cancellationToken.ThrowIfCancellationRequested();

                await DeleteWithRetryAsync(forbiddenPath, cancellationToken);
                PublishStatus($"Deleted {Path.GetFileName(forbiddenPath)}");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when filesystem events are noisy and checks are rescheduled.
        }
        catch (Exception ex)
        {
            PublishStatus($"Validation error: {ex.Message}");
        }
        finally
        {
            _validationLock.Release();
        }
    }

    private void ScheduleValidation()
    {
        if (!IsRunning)
        {
            return;
        }

        lock (_sync)
        {
            var delayMs = _config?.ValidationDelayMs > 0 ? _config.ValidationDelayMs : 2000;
            _validationTimer?.Change(delayMs, Timeout.Infinite);
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleValidation();
    }

    private void OnFolderRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleValidation();
    }

    private void OnValidationTimerTick(object? state)
    {
        _ = ValidateAndHealAsync();
    }

    private static async Task CopyWithRetryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var destinationFolder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                File.Copy(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (IsTransientFileError(ex) && attempt < 5)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static async Task DeleteWithRetryAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                File.Delete(path);
                return;
            }
            catch (Exception ex) when (IsTransientFileError(ex) && attempt < 5)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static bool IsTransientFileError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private static GuardianConfig CloneConfig(GuardianConfig config)
    {
        return new GuardianConfig
        {
            ObservedFolderPath = config.ObservedFolderPath.Trim(),
            SourceFolderPath = config.SourceFolderPath.Trim(),
            RequiredBaseName = config.RequiredBaseName.Trim(),
            RequiredExtensions = config.RequiredExtensions.ToArray(),
            ForbiddenBaseName = config.ForbiddenBaseName.Trim(),
            ValidationDelayMs = config.ValidationDelayMs > 0 ? config.ValidationDelayMs : 2000,
            StartWithWindows = config.StartWithWindows,
            StartMinimized = config.StartMinimized,
            AutoStartMonitoring = config.AutoStartMonitoring,
            CheckUpdatesOnLaunch = config.CheckUpdatesOnLaunch,
            GitHubRepository = config.GitHubRepository
        };
    }

    private void PublishStatus(string message)
    {
        StatusChanged?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void Dispose()
    {
        Stop();
        _validationLock.Dispose();
    }
}
