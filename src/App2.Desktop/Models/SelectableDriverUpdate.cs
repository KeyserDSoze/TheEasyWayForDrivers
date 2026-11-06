using System.ComponentModel;
using System.Runtime.CompilerServices;
using TheEasyWayForDrivers.Core.Formatting;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Desktop.Models;

public sealed class SelectableDriverUpdate(
    DriverUpdateInfo update) : INotifyPropertyChanged
{
    private bool _isSelected =
        DriverUpdateSelectionPolicy.ShouldSelectByDefault(
            update);

    public DriverUpdateInfo Update { get; } = update;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            // Details view and programmatic SelectAll must not be able to
            // select firmware, hidden/advanced or unknown-source candidates.
            _isSelected = value && CanQuickInstall;
            OnPropertyChanged();
        }
    }

    public string Title => Update.Title;

    public bool CanQuickInstall => QuickUpdatePolicy.IsEligible(Update);

    private string? _installedVersion;
    private string _installedVersionText = "Versione installata: non correlata per Hardware ID";

    public string InstalledVersionText => _installedVersionText;

    public void SetInstalledVersion(string? version)
    {
        var next = string.IsNullOrWhiteSpace(version)
            ? "Versione installata: non disponibile"
            : $"Versione installata: {version}";

        if (_installedVersionText == next)
            return;

        _installedVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        _installedVersionText = next;
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(CompactVersionSummary));
    }

    public string AvailableVersionText =>
        !string.IsNullOrWhiteSpace(Update.Version)
            ? $"Versione proposta: {Update.Version}"
            : WuaDriverVersionHint.FromTitle(Update.Title) is { } hint
                ? $"Versione nel titolo Windows Update: {hint} (da verificare)"
                : "Versione non indicata da Windows Update";

    public string CompactMetadata =>
        $"{Provider} · {SearchSource}" +
        (Update.SizeBytes is > 0 ? $" · {SizeText}" : string.Empty) +
        (Update.AdditionalHardwareIdsIncomplete
            ? " · associazioni hardware WUA incomplete"
            : string.Empty);

    /// <summary>
    /// Compact overview shows at most one version line. WUA generally does
    /// not expose a comparable driver package version; never invent one.
    /// </summary>
    public string CompactVersionSummary
    {
        get
        {
            var proposed = Update.Version;
            if (string.IsNullOrWhiteSpace(proposed))
            {
                var hint = WuaDriverVersionHint.FromTitle(Update.Title);
                if (hint is not null)
                    return _installedVersion is null
                        ? $"Versione nel titolo WU: {hint} (da verificare)"
                        : $"Sul PC: {_installedVersion} · Nel titolo WU: {hint} (da verificare)";

                return _installedVersion is null
                    ? "Versione del pacchetto non disponibile"
                    : $"Sul PC: {_installedVersion} · versione proposta non pubblicata";
            }

            return _installedVersion is null
                ? $"Versione proposta: {proposed} · versione sul PC non disponibile"
                : $"Sul PC: {_installedVersion} · Proposta: {proposed}";
        }
    }

    public string QuickInstallLabel =>
        CanQuickInstall ? "Aggiorna" : "Dettagli";


    public string Provider =>
        Update.Provider ??
        Update.Manufacturer ??
        "Windows Update";

    public string Model =>
        Update.Model ?? "—";

    public string DriverClass =>
        Update.DriverClass ?? "—";

    public string DriverDateText =>
        Update.DriverDate is { } date
            ? date.ToLocalTime().ToString("d")
            : "—";

    public string HardwareId =>
        Update.HardwareId ?? "—";

    public string KindText =>
        Update.IsHidden
            ? "Nascosto"
            : Update.IsAdvancedCandidate
                ? "Avanzato"
                : Update.IsOptional
                    ? "Facoltativo"
                    : "Consigliato";

    public string SearchSource =>
        Update.SearchSource;

    public string SizeText =>
        ByteSizeFormatter.Format(
            Update.SizeBytes);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
