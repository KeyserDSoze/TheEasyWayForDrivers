using System.ComponentModel;
using System.Runtime.CompilerServices;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Desktop.Models;

public sealed class DriverDeviceRow(
    DriverInfo driver) : INotifyPropertyChanged
{
    private IReadOnlyList<DriverUpdateInfo> _matchedUpdates = [];
    private IReadOnlyList<DriverUpdateInfo> _reviewOnlyUpdates = [];
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

    // These are ORDINARY WUA candidates only. A review-only association
    // must never enable the per-device install button or inflate Update All.
    public bool HasAvailableUpdate => _matchedUpdates.Count > 0;
    public bool HasReviewOnlyWuaCandidate => _reviewOnlyUpdates.Count > 0;
    public int ReviewOnlyCandidateCount => _reviewOnlyUpdates.Count;
    public string ReviewOnlyCandidateTitle =>
        _reviewOnlyUpdates.FirstOrDefault()?.Title ?? "—";
    public string ReviewOnlyCandidateSummary =>
        ReviewOnlyCandidateCount == 0
            ? "Nessun altro candidato WUA avanzato/nascosto correlato."
            : $"{ReviewOnlyCandidateCount} candidati WUA solo da verificare " +
              "(avanzati, nascosti o non selezionabili automaticamente). " +
              "Queste voci sono associate per ID, ma NON sono aggiornamenti " +
              "installabili con il pulsante del dispositivo o Aggiorna tutto. " +
              "Consulta Aggiornamenti disponibili → Avanzati, verifica modello, " +
              "versione, firma e indicazioni OEM.";
    public bool HasHardwareProblem => Driver.HasWindowsProblem;
    public string HardwareIdsText => Driver.HardwareIds.Count == 0 ? "Non disponibili" : string.Join(" · ", Driver.HardwareIds);
    public string CompatibleIdsText => Driver.CompatibleIds.Count == 0 ? "Non disponibili" : string.Join(" · ", Driver.CompatibleIds);
    public string HardwareIdentityHint =>
        HardwareIdentityParser.Describe(
            Driver.HardwareIds.FirstOrDefault() ?? Driver.CompatibleIds.FirstOrDefault());
    public int AvailableUpdateCount => _matchedUpdates.Count;
    public string DiscoveryGuidance =>
        HasAvailableUpdate
            ? "Windows Update ha trovato una corrispondenza per ID. Verifica comunque modello PC, versione Windows e driver prima di installare."
            : HasReviewOnlyWuaCandidate
                ? "Windows Update ha restituito uno o più candidati avanzati/nascosti correlati per ID: sola verifica, NON installabili da questa scheda. Consulta il filtro Avanzati."
            : !_hasSearchedUpdates
                ? "Esegui la ricerca approfondita dei driver; se non trovi risultati, consulta il supporto ufficiale del produttore."
                : Driver.PrimaryHardwareId == "Non disponibile"
                    ? "Nessun ID disponibile: verifica il dispositivo in Gestione dispositivi e usa il modello esatto del PC nel supporto OEM."
                    : "Nessun aggiornamento WUA correlato per ID. Copia l'Hardware ID e cerca il modello sul supporto ufficiale OEM; non installare pacchetti solo per somiglianza del nome.";

    public string NextAction =>
        HasReviewOnlyWuaCandidate && !HasAvailableUpdate &&
        Driver.ConfigManagerErrorCode != 22
            ? "Corrispondenza WUA avanzata/nascosta già rilevata. " +
              "Controlla la scheda Aggiornamenti disponibili (filtro Avanzati) " +
              "e il supporto OEM: non installare senza verifica di compatibilità."
            : DriverRemediationAdvisor.GetNextAction(
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
                : HasReviewOnlyWuaCandidate
                    ? "WUA da verificare"
                    : "OK";

    public string StatusBackground =>
        Status switch
        {
            "Driver mancante" => "#FDECEC",
            "Errore Windows" => "#FDECEC",
            "Da verificare" => "#FFF4CE",
            "Aggiornamento disponibile" => "#FFF4CE",
            "WUA da verificare" => "#FFF4CE",
            _ => "#E7F6EC"
        };

    public string StatusForeground =>
        Status switch
        {
            "Driver mancante" => "#A4262C",
            "Errore Windows" => "#A4262C",
            "Da verificare" => "#8A4B08",
            "Aggiornamento disponibile" => "#8A4B08",
            "WUA da verificare" => "#8A4B08",
            _ => "#107C10"
        };

    public string StatusDetail =>
        !Driver.HasDriver || Driver.ConfigManagerErrorCode != 0
            ? Driver.StatusDetail
            : HasAvailableUpdate
                ? AvailableUpdateCount == 1
                    ? "Windows Update propone un driver associato a un identificativo del dispositivo; verifica la compatibilità prima di installare."
                    : $"Windows Update propone {AvailableUpdateCount} driver associati agli identificativi; verifica la compatibilità prima di installare."
                : HasReviewOnlyWuaCandidate
                    ? "Il dispositivo ha corrispondenze WUA avanzate/nascoste per ID, " +
                      "ma NON sono aggiornamenti automaticamente installabili. " +
                      "Consulta il filtro Avanzati e la fonte OEM."
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

    public string CandidateAssessment =>
        PreferredUpdate is { } candidate
            ? DriverCandidateAssessment.Evaluate(Driver, candidate).Summary
            : "Nessun candidato Windows Update associato agli identificativi del dispositivo.";

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
        OnPropertyChanged(nameof(DiscoveryGuidance));
    }

    public void SetMatchedUpdates(
        IReadOnlyList<DriverUpdateInfo> updates,
        IReadOnlyList<DriverUpdateInfo>? reviewOnlyUpdates = null)
    {
        ArgumentNullException.ThrowIfNull(updates);
        // Safeguard against accidental UI promotion: even if a caller
        // supplies an advanced/unknown-source update as ordinary, it can
        // never become PreferredUpdate and enable one-click installation.
        _matchedUpdates = updates.Where(QuickUpdatePolicy.IsEligible).ToArray();
        _reviewOnlyUpdates = (reviewOnlyUpdates ?? [])
            .Where(update => !QuickUpdatePolicy.IsEligible(update))
            .ToArray();

        OnPropertyChanged(nameof(HasAvailableUpdate));
        OnPropertyChanged(nameof(HasReviewOnlyWuaCandidate));
        OnPropertyChanged(nameof(ReviewOnlyCandidateCount));
        OnPropertyChanged(nameof(ReviewOnlyCandidateTitle));
        OnPropertyChanged(nameof(ReviewOnlyCandidateSummary));
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
        OnPropertyChanged(nameof(CandidateAssessment));
        OnPropertyChanged(nameof(DiscoveryGuidance));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
