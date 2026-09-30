using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Data;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Desktop.Models;
using TheEasyWayForDrivers.Desktop.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using WpfClipboard = System.Windows.Clipboard;

namespace TheEasyWayForDrivers.Desktop;

public partial class MainWindow : Window
{
    private readonly DriverServiceClient _serviceClient = new();
    private readonly HttpClient _httpClient = new();
    private AppUpdateInfo? _availableAppUpdate;
    private bool _hasScannedDrivers;
    private bool _hasSearchedUpdates;
    private string? _lastNotifiedDriverUpdateFingerprint;

    public ObservableCollection<DriverDeviceRow> Drivers { get; } = [];
    public ObservableCollection<SelectableDriverUpdate> DriverUpdates { get; } = [];
    public ObservableCollection<OemProviderStatus> OemProviders { get; } = [];
    public ICollectionView DriversView { get; }

    public event Action<string, string>? TrayNotificationRequested;

    public MainWindow()
    {
        InitializeComponent();

        DriversView =
            CollectionViewSource.GetDefaultView(Drivers);
        DriversView.Filter = FilterDriver;
        DriversView.SortDescriptions.Add(
            new SortDescription(
                nameof(DriverDeviceRow.NeedsAttention),
                ListSortDirection.Descending));
        DriversView.SortDescriptions.Add(
            new SortDescription(
                nameof(DriverDeviceRow.HasAvailableUpdate),
                ListSortDirection.Descending));
        DriversView.SortDescriptions.Add(
            new SortDescription(
                nameof(DriverDeviceRow.Name),
                ListSortDirection.Ascending));

        DataContext = this;

        StatusFilterComboBox.SelectedIndex = 0;
        SourceFilterComboBox.SelectedIndex = 0;
        UpdateFilterSummary();

        var version =
            Assembly.GetExecutingAssembly().GetName().Version ??
            new Version(0, 0, 1);

        AppVersionText.Text = $"App {FormatVersion(version)}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshDiagnosticsAsync(CancellationToken.None);
            await RefreshOemProvidersCoreAsync(CancellationToken.None);
            SetStatus("Pronto. Premi “Controlla tutto” per una verifica completa.", 0);
        }
        catch (Exception)
        {
            SetServiceOffline();
            SetStatus(
                "Servizio non disponibile. Verifica l'installazione o l'avvio del servizio.",
                0);
        }
    }

    private async void ScanAllButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Avvio controllo completo...", 2);

            await RefreshDiagnosticsAsync(cancellationToken);
            await ScanDriversCoreAsync(cancellationToken);
            await SearchUpdatesCoreAsync(cancellationToken);
            await RefreshOemProvidersCoreAsync(cancellationToken);
            await CheckAppUpdateCoreAsync(cancellationToken);
            await RefreshDiagnosticsAsync(cancellationToken);

            var attentionCount = Drivers.Count(driver => driver.NeedsAttention);
            var appUpdateText = _availableAppUpdate?.IsUpdateAvailable == true
                ? $" App {_availableAppUpdate.LatestVersion} disponibile."
                : string.Empty;

            var applicableOemProviders =
                OemProviders.Count(provider => provider.IsApplicable);

            SetStatus(
                $"Controllo completo: {Drivers.Count} dispositivi, " +
                $"{attentionCount} da controllare, {DriverUpdates.Count} update driver, " +
                $"{applicableOemProviders} provider OEM applicabili." +
                appUpdateText,
                100);
        });
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(ScanDriversCoreAsync);
    }

    private async Task ScanDriversCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus("Scansione dei dispositivi e dei driver installati...", 10);
        var drivers = await _serviceClient.ScanAsync(cancellationToken);

        Drivers.Clear();
        foreach (var driver in drivers)
        {
            Drivers.Add(new DriverDeviceRow(driver));
        }

        ApplyUpdateMatches();
        ApplySystemOemSource();
        _hasScannedDrivers = true;
        UpdateSummaryCards();

        var attentionCount = Drivers.Count(driver => driver.NeedsAttention);
        SetStatus(
            $"Scansione completata: {drivers.Count} dispositivi, {attentionCount} da controllare.",
            100);
    }

    private async void SearchUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(SearchUpdatesCoreAsync);
    }

    private async Task SearchUpdatesCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus("Ricerca degli aggiornamenti driver tramite Windows Update...", 10);
        var updates = await _serviceClient.SearchUpdatesAsync(cancellationToken);

        DriverUpdates.Clear();
        foreach (var update in updates)
        {
            DriverUpdates.Add(new SelectableDriverUpdate(update));
        }

        ApplyUpdateMatches();
        _hasSearchedUpdates = true;
        UpdateSummaryCards();

        var matchedDevices = Drivers.Count(driver => driver.HasAvailableUpdate);
        NotifyDriverUpdatesIfChanged(
            updates,
            matchedDevices);

        SetStatus(
            $"Trovati {updates.Count} aggiornamenti driver; " +
            $"{matchedDevices} dispositivi correlati per hardware ID.",
            100);
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

            await RefreshUpdatesAsync(cancellationToken);
            await ScanDriversCoreAsync(cancellationToken);
            await RefreshDiagnosticsAsync(cancellationToken);

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
        });
    }

    private async void CheckAppUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(CheckAppUpdateCoreAsync);
    }

    private async Task CheckAppUpdateCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus("Il servizio sta controllando l'ultima release disponibile...", 20);

        _availableAppUpdate =
            await _serviceClient.CheckAppUpdateAsync(cancellationToken);

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
    }

    private async void RefreshOemProvidersButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Verifica dei provider OEM ufficiali...", 20);
            await RefreshOemProvidersCoreAsync(cancellationToken);

            var applicable =
                OemProviders.Count(provider => provider.IsApplicable);

            SetStatus(
                $"Provider OEM aggiornati: {applicable} applicabili al sistema.",
                100);
        });
    }

    private void OpenSelectedOemProviderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (OemProvidersGrid.SelectedItem is not OemProviderStatus provider)
        {
            MessageBox.Show(
                this,
                "Seleziona un provider OEM.",
                "TheEasyWayForDrivers",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!Uri.TryCreate(
                provider.OfficialSupportUrl,
                UriKind.Absolute,
                out var supportUri) ||
            supportUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(
                this,
                "Il provider non espone un URL di supporto HTTPS valido.",
                "TheEasyWayForDrivers",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = supportUri.AbsoluteUri,
            UseShellExecute = true
        });

        SetStatus(
            $"Aperto il supporto ufficiale {provider.DisplayName}.",
            100);
    }

    private async Task RefreshOemProvidersCoreAsync(
        CancellationToken cancellationToken)
    {
        var providers =
            await _serviceClient.GetOemProvidersAsync(
                cancellationToken);

        OemProviders.Clear();

        foreach (var provider in providers)
        {
            OemProviders.Add(provider);
        }

        ApplySystemOemSource();

        if (OemProvidersGrid.SelectedItem is null &&
            OemProviders.Count > 0)
        {
            OemProvidersGrid.SelectedIndex = 0;
        }
    }

    private async void RefreshDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Aggiornamento diagnostica del servizio...", 20);
            await RefreshDiagnosticsAsync(cancellationToken);
            SetStatus("Diagnostica aggiornata.", 100);
        });
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DiagnosticsLogTextBox.Text))
        {
            return;
        }

        WpfClipboard.SetText(DiagnosticsLogTextBox.Text);
        SetStatus("Log diagnostico copiato negli appunti.", 100);
    }

    private async Task RefreshDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var diagnostics =
            await _serviceClient.GetDiagnosticsAsync(cancellationToken);

        ServiceHealthText.Text = "Online";
        ServiceVersionCardText.Text = $"v{diagnostics.ServiceVersion}";
        HeaderServiceText.Text = $"Servizio online · v{diagnostics.ServiceVersion}";

        ServiceVersionText.Text = diagnostics.ServiceVersion;
        ServiceStartedText.Text =
            diagnostics.StartedAt.ToLocalTime().ToString("g");
        ServiceLogPathText.Text =
            diagnostics.CurrentLogFile ?? diagnostics.LogDirectory;

        DiagnosticsLogTextBox.Text =
            diagnostics.RecentLogLines.Count == 0
                ? "Nessuna riga di log disponibile."
                : string.Join(Environment.NewLine, diagnostics.RecentLogLines);

        DiagnosticsLogTextBox.ScrollToEnd();
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

            SetStatus("Verifica integrità SHA-256 dell'aggiornamento...", 100);
            await VerifyFileSha256Async(
                tempPath,
                _availableAppUpdate.Sha256Digest,
                cancellationToken);

            SetStatus("Integrità verificata. Avvio dell'aggiornamento...", 100);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "--update",
                UseShellExecute = true,
                Verb = "runas"
            });

            System.Windows.Application.Current.Shutdown();
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

        ApplyUpdateMatches();
        _hasSearchedUpdates = true;
        UpdateSummaryCards();
    }

    private void ApplySystemOemSource()
    {
        var systemOem =
            OemProviders.FirstOrDefault(provider =>
                string.Equals(
                    provider.ProviderId,
                    "system-oem",
                    StringComparison.OrdinalIgnoreCase) &&
                provider.IsApplicable);

        var displayName =
            systemOem?.DisplayName;

        foreach (var driver in Drivers)
        {
            driver.SetSystemOem(displayName);
        }

        RefreshDriversView();
    }

    private void ApplyUpdateMatches()
    {
        var updates = DriverUpdates
            .Select(item => item.Update)
            .ToArray();

        foreach (var driver in Drivers)
        {
            driver.SetMatchedUpdates(
                DriverUpdateMatcher.FindMatches(
                    driver.Driver,
                    updates));
        }

        RefreshDriversView();
    }

    private void DriverFilter_Changed(
        object sender,
        RoutedEventArgs e) =>
        RefreshDriversView();

    private void DeviceSearchTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e) =>
        RefreshDriversView();

    private bool FilterDriver(object item)
    {
        if (item is not DriverDeviceRow driver)
        {
            return false;
        }

        var search =
            DeviceSearchTextBox?.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(search) &&
            !ContainsSearchText(driver, search))
        {
            return false;
        }

        var statusFilter =
            StatusFilterComboBox?.SelectedValue?.ToString();

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            !string.Equals(
                statusFilter,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    statusFilter,
                    "attention",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!driver.NeedsAttention)
                {
                    return false;
                }
            }
            else if (!string.Equals(
                         driver.Status,
                         statusFilter,
                         StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var sourceFilter =
            SourceFilterComboBox?.SelectedValue?.ToString();

        if (!string.IsNullOrWhiteSpace(sourceFilter) &&
            !string.Equals(
                sourceFilter,
                "all",
                StringComparison.OrdinalIgnoreCase) &&
            !driver.RecommendedSource.Contains(
                sourceFilter,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsSearchText(
        DriverDeviceRow driver,
        string search)
    {
        var fields = new[]
        {
            driver.Name,
            driver.Manufacturer,
            driver.DeviceClass,
            driver.DriverProvider,
            driver.DriverVersion,
            driver.DeviceId,
            driver.RecommendedSource,
            driver.AvailableUpdateTitle,
            driver.AvailableUpdateProvider,
            driver.AvailableUpdateModel
        };

        return fields.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(
                search,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private void RefreshDriversView()
    {
        if (DriversView is null)
        {
            return;
        }

        DriversView.Refresh();
        UpdateFilterSummary();
    }

    private void UpdateFilterSummary()
    {
        if (FilteredDevicesCountText is null ||
            DriversView is null)
        {
            return;
        }

        var visibleCount =
            DriversView.Cast<object>().Count();

        FilteredDevicesCountText.Text =
            $"Visualizzati {visibleCount} di {Drivers.Count}";
    }

    private void NotifyDriverUpdatesIfChanged(
        IReadOnlyList<DriverUpdateInfo> updates,
        int matchedDevices)
    {
        if (updates.Count == 0)
        {
            _lastNotifiedDriverUpdateFingerprint = null;
            return;
        }

        var fingerprint =
            DriverUpdateFingerprint.Create(updates);

        if (string.Equals(
                fingerprint,
                _lastNotifiedDriverUpdateFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastNotifiedDriverUpdateFingerprint =
            fingerprint;

        var deviceText =
            matchedDevices == 1
                ? "1 dispositivo correlato"
                : $"{matchedDevices} dispositivi correlati";

        TrayNotificationRequested?.Invoke(
            "Aggiornamenti driver disponibili",
            $"Trovati {updates.Count} aggiornamenti Windows Update · {deviceText}.");
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
            SetServiceOffline();
            SetStatus(
                "Il servizio non risponde. Installa o avvia il servizio e riprova.",
                0);
        }
        catch (IOException exception)
        {
            SetServiceOffline();
            SetStatus($"Connessione al servizio interrotta: {exception.Message}", 0);
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
        ScanAllButton.IsEnabled = enabled;
        ScanButton.IsEnabled = enabled;
        SearchUpdatesButton.IsEnabled = enabled;
        InstallSelectedButton.IsEnabled = enabled;
        CheckAppUpdateButton.IsEnabled = enabled;
        ApplyAppUpdateButton.IsEnabled = enabled;
        RefreshOemProvidersButton.IsEnabled = enabled;
        OpenSelectedOemProviderButton.IsEnabled = enabled;
        RefreshDiagnosticsButton.IsEnabled = enabled;
    }

    private void UpdateSummaryCards()
    {
        DevicesCountText.Text =
            _hasScannedDrivers ? Drivers.Count.ToString() : "—";

        UpdateFilterSummary();

        AttentionCountText.Text =
            _hasScannedDrivers
                ? Drivers.Count(driver => driver.NeedsAttention).ToString()
                : "—";

        UpdatesCountText.Text =
            _hasSearchedUpdates ? DriverUpdates.Count.ToString() : "—";
    }

    private void SetServiceOffline()
    {
        ServiceHealthText.Text = "Offline";
        ServiceVersionCardText.Text = string.Empty;
        HeaderServiceText.Text = "Servizio non disponibile";
        ServiceVersionText.Text = "—";
        ServiceStartedText.Text = "—";
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
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("TheEasyWayForDrivers", "updater"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalLength = response.Content.Headers.ContentLength;
        await using var source =
            await response.Content.ReadAsStreamAsync(cancellationToken);
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

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);

            downloaded += read;

            if (totalLength is > 0)
            {
                progress(
                    (int)Math.Clamp(
                        downloaded * 100 / totalLength.Value,
                        0,
                        100));
            }
        }

        progress(100);
    }

    private static async Task VerifyFileSha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            useAsync: true);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        var actualSha256 = Convert.ToHexString(hash).ToLowerInvariant();

        if (string.Equals(
            actualSha256,
            expectedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        throw new InvalidDataException(
            "L'aggiornamento scaricato non corrisponde al digest SHA-256 " +
            "pubblicato da GitHub. Il file è stato rifiutato.");
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
