using System.ComponentModel;
using System.Windows;
using TheEasyWayForDrivers.Desktop.Tray;

namespace TheEasyWayForDrivers.Desktop;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TrayIconController? _trayIcon;
    private bool _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mainWindow = new MainWindow();
        _mainWindow.Closing += OnMainWindowClosing;

        _trayIcon = new TrayIconController(
            showWindow: ShowMainWindow,
            exit: ExitApplication);

        _mainWindow.Show();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _trayIcon?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
