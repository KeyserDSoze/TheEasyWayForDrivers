using System.Windows;
using TheEasyWayForDrivers.Desktop.Services;
using TheEasyWayForDrivers.Desktop.Settings;

namespace TheEasyWayForDrivers.Desktop;

public partial class FirstRunWindow : Window
{
    private readonly DesktopSettingsService _settingsService;

    public FirstRunWindow(
        DesktopSettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;

        InitializeComponent();

        var current =
            _settingsService.Current;

        StartWithWindowsCheckBox.IsChecked =
            current.StartWithWindows;

        ShowNotificationsCheckBox.IsChecked =
            current.ShowNotifications;

        CheckOnStartupCheckBox.IsChecked =
            current.CheckOnStartup;
    }

    private void ContinueButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var current =
                _settingsService.Current;

            _settingsService.Save(
                current with
                {
                    StartWithWindows =
                        StartWithWindowsCheckBox.IsChecked == true,
                    ShowNotifications =
                        ShowNotificationsCheckBox.IsChecked == true,
                    CheckOnStartup =
                        CheckOnStartupCheckBox.IsChecked == true,
                    FirstRunCompleted = true
                });

            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "OmegaDrive",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
