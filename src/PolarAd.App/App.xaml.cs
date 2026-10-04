using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PolarAd.Core;

namespace PolarAd.App;

public partial class App : System.Windows.Application
{
    public ProtectionService ProtectionService { get; private set; } = null!;
    private TrayIconManager? _tray;
    private Mutex? _singleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // AutoStartManager registers BOTH a Scheduled Task (onlogon trigger)
        // AND a Startup-folder shortcut that invokes the same task, so Task
        // Manager's Startup tab shows PolarAd — see AutoStartManager's class
        // comment. That means two launches can race at logon; the second one
        // exits here instead of trying to bind port 53 twice.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\PolarAd_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ProtectionService = new ProtectionService();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeSynchronousRestore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => SafeSynchronousRestore();
        DispatcherUnhandledException += (_, args) =>
        {
            SafeSynchronousRestore();
            // Let the default handler still report the crash; we only guarantee
            // the network is not left pointed at a dead DNS proxy.
        };
        SessionEnding += (_, _) => SafeSynchronousRestore();

        await ProtectionService.ReconcileStartupStateAsync();

        var mainWindow = new MainWindow(ProtectionService);
        _tray = new TrayIconManager(ProtectionService, mainWindow);
        ProtectionService.QueryObserved += _ => Dispatcher.Invoke(() => _tray?.UpdateStatusText());

        mainWindow.Show();
    }

    private void SafeSynchronousRestore()
    {
        try
        {
            if (ProtectionService?.IsRunning == true)
            {
                ProtectionService.ShutdownAsync().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Last-resort safety net; README documents the manual recovery script
            // (scripts/restore-dns.ps1) for the rare case even this doesn't run.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SafeSynchronousRestore();
        _tray?.Dispose();
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
