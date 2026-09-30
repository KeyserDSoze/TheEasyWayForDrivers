using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace TheEasyWayForDrivers.Desktop.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _showWindow;
    private readonly Icon? _applicationIcon;

    public TrayIconController(
        Action showWindow,
        Action exit)
    {
        _showWindow = showWindow;

        var executablePath =
            Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(executablePath) &&
            File.Exists(executablePath))
        {
            _applicationIcon =
                Icon.ExtractAssociatedIcon(
                    executablePath!);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(
            "Apri OmegaDrive",
            null,
            (_, _) => _showWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(
            "Esci",
            null,
            (_, _) => exit());

        _notifyIcon = new NotifyIcon
        {
            Text = "OmegaDrive Driver Manager",
            Icon = _applicationIcon ?? SystemIcons.Shield,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick +=
            (_, _) => _showWindow();

        _notifyIcon.BalloonTipClicked +=
            (_, _) => _showWindow();
    }

    public void ShowNotification(
        string title,
        string message)
    {
        _notifyIcon.ShowBalloonTip(
            5000,
            title,
            message,
            ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _applicationIcon?.Dispose();
        GC.SuppressFinalize(this);
    }
}
