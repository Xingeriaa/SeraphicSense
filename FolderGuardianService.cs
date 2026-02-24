using System.IO;
using System.Threading;

namespace SeraphicSense;

public sealed class FolderGuardianService : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private int _pendingValidation;

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _validationTimer;
    private CancellationTokenSource? _lifetimeCts;
    private GuardianConfig? _config;
    private bool _isDisposed;

    public bool IsRunning { get; private set; }
    public event Action<string>? StatusChanged;

    public void Start(GuardianConfig config)
    {
        ThrowIfDisposed();

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
        _lifetimeCts = new CancellationTokenSource();

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
        _pendingValidation = 0;

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
        _pendingValidation = 0;
        _lifetimeCts?.Cancel();

        lock (_sync)
        {
            if (_validationTimer is not null)
            {
                try
                {
                    _validationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // Timer may already be disposed during shutdown race.
                }

                _validationTimer.Dispose();
                _validationTimer = null;
            }
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

        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        PublishStatus("Monitoring stopped.");
    }

    public async Task ValidateAndHealAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed || !IsRunning || _config is null)
        {
            return;
        }

        CancellationToken effectiveToken;
        try
        {
            effectiveToken = _lifetimeCts?.Token ?? cancellationToken;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        var lockAcquired = false;
        try
        {
            lockAcquired = await _validationLock.WaitAsync(0, effectiveToken);
            if (!lockAcquired)
            {
                Interlocked.Exchange(ref _pendingValidation, 1);
                return;
            }

            foreach (var extension in _config.RequiredExtensions)
            {
                effectiveToken.ThrowIfCancellationRequested();

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

                await CopyWithRetryAsync(sourcePath, destinationPath, effectiveToken);
                PublishStatus($"Restored {requiredFileName}");
            }

            var forbiddenPattern = $"{_config.ForbiddenBaseName}*";
            foreach (var forbiddenPath in Directory.EnumerateFiles(_config.ObservedFolderPath, forbiddenPattern))
            {
                effectiveToken.ThrowIfCancellationRequested();

                await DeleteWithRetryAsync(forbiddenPath, effectiveToken);
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
            if (lockAcquired)
            {
                try
                {
                    _validationLock.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Expected only when service is tearing down.
                }
            }

            if (!_isDisposed && IsRunning && Interlocked.Exchange(ref _pendingValidation, 0) == 1)
            {
                ScheduleValidation();
            }
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
        if (_isDisposed || !IsRunning)
        {
            return;
        }

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
            GitHubRepository = config.GitHubRepository,
            LastAppliedDataReleaseTag = config.LastAppliedDataReleaseTag
        };
    }

    private void PublishStatus(string message)
    {
        StatusChanged?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        _validationLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
