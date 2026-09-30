using System.ComponentModel;
using System.Windows;
using TheEasyWayForDrivers.Desktop.Services;
using TheEasyWayForDrivers.Desktop.Tray;

namespace TheEasyWayForDrivers.Desktop;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TrayIconController? _trayIcon;
    private DesktopSettingsService? _settingsService;
    private bool _exitRequested;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService =
            new DesktopSettingsService();

        _mainWindow =
            new MainWindow(_settingsService);

        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.StateChanged += OnMainWindowStateChanged;
        _mainWindow.TrayNotificationRequested +=
            OnTrayNotificationRequested;

        _trayIcon = new TrayIconController(
            showWindow: ShowMainWindow,
            exit: ExitApplication);

        _mainWindow.Show();
    }

    private void OnMainWindowClosing(
        object? sender,
        CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        if (_settingsService?.Current.CloseToTray != true)
        {
            _exitRequested = true;
            _trayIcon?.Dispose();
            Dispatcher.BeginInvoke(new Action(Shutdown));
            return;
        }

        e.Cancel = true;
        HideMainWindowToTray();
    }

    private void OnMainWindowStateChanged(
        object? sender,
        EventArgs e)
    {
        if (_mainWindow?.WindowState !=
            WindowState.Minimized ||
            _settingsService?.Current.MinimizeToTray != true)
        {
            return;
        }

        HideMainWindowToTray();
    }

    private void HideMainWindowToTray()
    {
        _mainWindow?.Hide();

        if (_trayHintShown ||
            _settingsService?.Current.ShowNotifications != true)
        {
            return;
        }

        _trayHintShown = true;

        _trayIcon?.ShowNotification(
            "TheEasyWayForDrivers continua in background",
            "L'app resta nell'area di notifica. Fai doppio clic sull'icona per riaprirla.");
    }

    private void OnTrayNotificationRequested(
        string title,
        string message)
    {
        if (_settingsService?.Current.ShowNotifications != true)
        {
            return;
        }

        _trayIcon?.ShowNotification(
            title,
            message);
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
