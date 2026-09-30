using System.Drawing;
using System.Windows.Forms;

namespace TheEasyWayForDrivers.Desktop.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconController(Action showWindow, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(
            "Apri TheEasyWayForDrivers",
            null,
            (_, _) => showWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(
            "Esci",
            null,
            (_, _) => exit());

        _notifyIcon = new NotifyIcon
        {
            Text = "TheEasyWayForDrivers",
            Icon = SystemIcons.Shield,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick +=
            (_, _) => showWindow();
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
        GC.SuppressFinalize(this);
    }
}
