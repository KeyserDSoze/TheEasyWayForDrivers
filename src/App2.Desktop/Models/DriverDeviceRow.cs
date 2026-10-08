using System.ComponentModel;
using System.Runtime.CompilerServices;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Desktop.Models;

public sealed class DriverDeviceRow(
    DriverInfo driver) : INotifyPropertyChanged
{
    private IReadOnlyList<DriverUpdateInfo> _matchedUpdates = [];
    private string? _systemOemDisplayName;
    private bool _hasSearchedUpdates;

    public DriverInfo Driver { get; } = driver;

    public string DeviceId => Driver.DeviceId;
    public string Name => Driver.Name;
    public string? Manufacturer => Driver.Manufacturer;
    public string? DeviceClass => Driver.DeviceClass;
    public string? DriverProvider => Driver.DriverProvider;
    public string? DriverVersion => Driver.DriverVersion;
    public DateTimeOffset? DriverDate => Driver.DriverDate;
    public string? InfName => Driver.InfName;
    public uint ConfigManagerErrorCode => Driver.ConfigManagerErrorCode;

    public bool HasAvailableUpdate => _matchedUpdates.Count > 0;
    public bool HasHardwareProblem => Driver.NeedsAttention;
    public string HardwareIdsText => Driver.HardwareIds.Count == 0 ? "Non disponibili" : string.Join(" · ", Driver.HardwareIds);
    public string CompatibleIdsText => Driver.CompatibleIds.Count == 0 ? "Non disponibili" : string.Join(" · ", Driver.CompatibleIds);
    public int AvailableUpdateCount => _matchedUpdates.Count;

    public string NextAction =>
        DriverRemediationAdvisor.GetNextAction(
            Driver,
            HasAvailableUpdate,
            _hasSearchedUpdates,
            RecommendedSource);

    public DriverUpdateInfo? PreferredUpdate =>
        _matchedUpdates.FirstOrDefault();

    public string RecommendedSource =>
        DriverSourceAdvisor.GetSource(
            Driver,
            _matchedUpdates,
            _systemOemDisplayName);

    public bool NeedsAttention =>
        Driver.NeedsAttention || HasAvailableUpdate;

    public string Status =>
        Driver.NeedsAttention
            ? Driver.Status
            : HasAvailableUpdate
                ? "Aggiornamento disponibile"
                : "OK";

    public string StatusBackground =>
        Status switch
        {
            "Driver mancante" => "#FDECEC",
            "Errore Windows" => "#FDECEC",
            "Da verificare" => "#FFF4CE",
            "Aggiornamento disponibile" => "#FFF4CE",
            _ => "#E7F6EC"
        };

    public string StatusForeground =>
        Status switch
        {
            "Driver mancante" => "#A4262C",
            "Errore Windows" => "#A4262C",
            "Da verificare" => "#8A4B08",
            "Aggiornamento disponibile" => "#8A4B08",
            _ => "#107C10"
        };

    public string StatusDetail =>
        !Driver.HasDriver || Driver.ConfigManagerErrorCode != 0
            ? Driver.StatusDetail
            : HasAvailableUpdate
                ? AvailableUpdateCount == 1
                    ? "Windows Update propone un driver compatibile con questo dispositivo."
                    : $"Windows Update propone {AvailableUpdateCount} driver compatibili con questo dispositivo."
                : Driver.StatusDetail;

    public string AvailableUpdateTitle =>
        PreferredUpdate?.Title ?? "—";

    public string AvailableUpdateProvider =>
        PreferredUpdate?.Provider ??
        PreferredUpdate?.Manufacturer ??
        "—";

    public string AvailableUpdateModel =>
        PreferredUpdate?.Model ?? "—";

    public string AvailableUpdateDate =>
        PreferredUpdate?.DriverDate is { } date
            ? date.ToLocalTime().ToString("d")
            : "—";

    public string MatchedHardwareId =>
        PreferredUpdate?.HardwareId ?? "—";

    public string MatchEvidence =>
        PreferredUpdate is null
            ? "Nessun update correlato"
            : DriverUpdateMatcher.MatchStrength(Driver, PreferredUpdate) switch
            {
                2 => "Hardware ID esatto (compatibilità da verificare)",
                1 => "Compatible ID (verifica aggiuntiva necessaria)",
                _ => "Associazione non verificata"
            };

    public void SetSystemOem(
        string? displayName)
    {
        _systemOemDisplayName = displayName;
        OnPropertyChanged(nameof(RecommendedSource));
        OnPropertyChanged(nameof(NextAction));
    }

    public void SetSearchCompleted(bool completed)
    {
        _hasSearchedUpdates = completed;
        OnPropertyChanged(nameof(NextAction));
    }

    public void SetMatchedUpdates(
        IReadOnlyList<DriverUpdateInfo> updates)
    {
        _matchedUpdates = updates;

        OnPropertyChanged(nameof(HasAvailableUpdate));
        OnPropertyChanged(nameof(AvailableUpdateCount));
        OnPropertyChanged(nameof(PreferredUpdate));
        OnPropertyChanged(nameof(RecommendedSource));
        OnPropertyChanged(nameof(NextAction));
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusBackground));
        OnPropertyChanged(nameof(StatusForeground));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(AvailableUpdateTitle));
        OnPropertyChanged(nameof(AvailableUpdateProvider));
        OnPropertyChanged(nameof(AvailableUpdateModel));
        OnPropertyChanged(nameof(AvailableUpdateDate));
        OnPropertyChanged(nameof(MatchedHardwareId));
        OnPropertyChanged(nameof(MatchEvidence));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
