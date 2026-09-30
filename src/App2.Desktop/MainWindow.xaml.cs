using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Windows;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Desktop.Models;
using TheEasyWayForDrivers.Desktop.Services;

namespace TheEasyWayForDrivers.Desktop;

public partial class MainWindow : Window
{
    private readonly DriverServiceClient _serviceClient = new();
    private readonly HttpClient _httpClient = new();
    private AppUpdateInfo? _availableAppUpdate;

    public ObservableCollection<DriverInfo> Drivers { get; } = [];
    public ObservableCollection<SelectableDriverUpdate> DriverUpdates { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Scansione dei dispositivi e dei driver installati...", 10);
            var drivers = await _serviceClient.ScanAsync(cancellationToken);

            Drivers.Clear();
            foreach (var driver in drivers)
            {
                Drivers.Add(driver);
            }

            var attentionCount = drivers.Count(driver => driver.NeedsAttention);
            SetStatus(
                $"Scansione completata: {drivers.Count} dispositivi, {attentionCount} da controllare.",
                100);
        });
    }

    private async void SearchUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Ricerca degli aggiornamenti driver tramite Windows Update...", 10);
            var updates = await _serviceClient.SearchUpdatesAsync(cancellationToken);

            DriverUpdates.Clear();
            foreach (var update in updates)
            {
                DriverUpdates.Add(new SelectableDriverUpdate(update));
            }

            SetStatus($"Trovati {updates.Count} aggiornamenti driver.", 100);
        });
    }

    private async void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = DriverUpdates
            .Where(item => item.IsSelected)
            .Select(item => item.Update.Id)
            .ToArray();

        if (selected.Length == 0)
        {
            MessageBox.Show(
                this,
                "Seleziona almeno un aggiornamento driver.",
                "TheEasyWayForDrivers",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            RebootButton.Visibility = Visibility.Collapsed;

            var result = await _serviceClient.InstallUpdatesAsync(
                selected,
                progress => Dispatcher.Invoke(() =>
                    SetStatus(progress.Message, progress.Percent)),
                cancellationToken);

            SetStatus(result.Message, 100);

            if (result.RebootRequired)
            {
                RebootButton.Visibility = Visibility.Visible;
                MessageBox.Show(
                    this,
                    "L'installazione è terminata e Windows richiede un riavvio. " +
                    "Puoi riavviare ora con il pulsante dedicato oppure farlo più tardi.",
                    "Riavvio richiesto",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            await RefreshUpdatesAsync(cancellationToken);
        });
    }

    private async void CheckAppUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Il servizio sta controllando l'ultima release disponibile...", 20);

            _availableAppUpdate = await _serviceClient.CheckAppUpdateAsync(cancellationToken);

            if (_availableAppUpdate?.IsUpdateAvailable == true)
            {
                ApplyAppUpdateButton.Visibility = Visibility.Visible;
                SetStatus(
                    $"Nuova versione disponibile: {_availableAppUpdate.LatestVersion}.",
                    100);
            }
            else
            {
                ApplyAppUpdateButton.Visibility = Visibility.Collapsed;
                SetStatus("Servizio e applicazione sono aggiornati.", 100);
            }
        });
    }

    private async void ApplyAppUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableAppUpdate is null || !_availableAppUpdate.IsUpdateAvailable)
        {
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Download del nuovo installer...", 0);

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"TheEasyWayForDrivers-Setup-{_availableAppUpdate.LatestVersion}.exe");

            await DownloadFileAsync(
                _availableAppUpdate.DownloadUrl,
                tempPath,
                percent => Dispatcher.Invoke(() =>
                    SetStatus($"Download aggiornamento: {percent}%.", percent)),
                cancellationToken);

            SetStatus("Avvio dell'aggiornamento...", 100);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "--update",
                UseShellExecute = true,
                Verb = "runas"
            });

            Application.Current.Shutdown();
        });
    }

    private void RebootButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Riavviare Windows adesso?",
            "Conferma riavvio",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 0",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
    }

    private async Task RefreshUpdatesAsync(CancellationToken cancellationToken)
    {
        var updates = await _serviceClient.SearchUpdatesAsync(cancellationToken);
        DriverUpdates.Clear();

        foreach (var update in updates)
        {
            DriverUpdates.Add(new SelectableDriverUpdate(update));
        }
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> operation)
    {
        SetButtonsEnabled(false);

        try
        {
            await operation(CancellationToken.None);
        }
        catch (TimeoutException)
        {
            SetStatus("Il servizio non risponde. Installa o avvia il servizio e riprova.", 0);
        }
        catch (Exception exception)
        {
            SetStatus($"Errore: {exception.Message}", 0);
            MessageBox.Show(
                this,
                exception.Message,
                "TheEasyWayForDrivers",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        ScanButton.IsEnabled = enabled;
        SearchUpdatesButton.IsEnabled = enabled;
        InstallSelectedButton.IsEnabled = enabled;
        CheckAppUpdateButton.IsEnabled = enabled;
        ApplyAppUpdateButton.IsEnabled = enabled;
    }

    private void SetStatus(string message, int percent)
    {
        StatusText.Text = message;
        OperationProgress.Value = Math.Clamp(percent, 0, 100);
    }

    private async Task DownloadFileAsync(
        string url,
        string targetPath,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TheEasyWayForDrivers", "updater"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalLength = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(targetPath);

        var buffer = new byte[1024 * 128];
        long downloaded = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            if (totalLength is > 0)
            {
                progress((int)Math.Clamp(downloaded * 100 / totalLength.Value, 0, 100));
            }
        }

        progress(100);
    }
}
