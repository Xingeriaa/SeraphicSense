using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SeraphicSense;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\SeraphicSense.Instance";
    private const string ActivateEventName = @"Local\SeraphicSense.Activate";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWaitHandle;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            TrySignalRunningInstance();
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateEventName);

        _activateWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            waitObject: _activateEvent,
            callBack: (_, _) =>
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.ShowFromExternalActivation();
                    }
                });
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        _activateWaitHandle?.Unregister(null);
        _activateWaitHandle = null;

        _activateEvent?.Dispose();
        _activateEvent = null;

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ignore if mutex ownership was already lost during shutdown.
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    private static void TrySignalRunningInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
            signal.Set();
        }
        catch
        {
            // Best effort: existing instance may still be starting.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        e.Handled = true;

        System.Windows.MessageBox.Show(
            "SeraphicSense encountered an unexpected error and will close.\nCheck crash.log in %AppData%\\SeraphicSense for details.",
            "SeraphicSense Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown(-1);
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception("Non-Exception unhandled error.");
        WriteCrashLog("AppDomainUnhandledException", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            AppPaths.EnsureAppDirectories();
            var logPath = Path.Combine(AppPaths.AppDataDirectory, "crash.log");
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, message);
        }
        catch
        {
            // Suppress logging failures.
        }
    }
}
