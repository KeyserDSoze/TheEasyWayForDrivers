using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Explains whether a discovery result can be attributed to an individual
/// Windows device. Catalog-level OEM packages do NOT contain per-device IDs.
/// </summary>
public static class DiscoveryDeviceEvidence
{
    public static int MatchStrength(DiscoveryResult result, DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(device);

        if (result.Kind != DiscoveryResultKind.WindowsUpdateDriver)
            return 0;

        // Reuse the Home matching policy: the first WUA DriverHardwareID
        // is not exhaustive, and an update with no primary ID may still
        // have a confirmed ID in its additional device entries.
        return DriverUpdateMatcher.MatchStrength(
            device, result.HardwareId, result.AdditionalHardwareIds);
    }

    public static string Explain(DiscoveryResult result, DriverInfo device)
    {
        var strength = MatchStrength(result, device);
        return strength switch
        {
            2 => "Hardware ID esatto: candidato associato, non compatibilità certificata.",
            1 => "Compatible ID: associazione più generica; verifica manuale richiesta.",
            _ when result.Kind != DiscoveryResultKind.WindowsUpdateDriver =>
                "Pacchetto OEM a livello modello/catalogo: nessuna associazione verificata al singolo dispositivo.",
            _ when result.WuaIdsIncomplete =>
                "Metadati WUA delle associazioni incompleti: nessun ID correlato fra quelli letti; assenza non verificabile.",
            _ => "Nessuna corrispondenza Hardware ID/Compatible ID con il dispositivo selezionato."
        };
    }

    public static bool IsDeviceCandidate(DiscoveryResult result, DriverInfo device) =>
        MatchStrength(result, device) > 0;
}
