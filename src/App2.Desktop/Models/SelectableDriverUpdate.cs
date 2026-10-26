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

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string Title => Update.Title;

    public bool CanQuickInstall => QuickUpdatePolicy.IsEligible(Update);

    private string _installedVersionText = "Versione installata: non correlata per Hardware ID";

    public string InstalledVersionText => _installedVersionText;

    public void SetInstalledVersion(string? version)
    {
        var next = string.IsNullOrWhiteSpace(version)
            ? "Versione installata: non disponibile"
            : $"Versione installata: {version}";

        if (_installedVersionText == next)
            return;

        _installedVersionText = next;
        OnPropertyChanged(nameof(InstalledVersionText));
    }

    public string AvailableVersionText =>
        string.IsNullOrWhiteSpace(Update.Version)
            ? "Versione non indicata da Windows Update"
            : $"Versione proposta: {Update.Version}";

    public string CompactMetadata =>
        $"{KindText} · {Provider} · {SizeText} · {SearchSource}";

    public string QuickInstallLabel =>
        CanQuickInstall ? "Aggiorna" : "Dettagli avanzati";


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
