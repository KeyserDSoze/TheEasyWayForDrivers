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

        if (result.Kind != DiscoveryResultKind.WindowsUpdateDriver ||
            string.IsNullOrWhiteSpace(result.HardwareId))
            return 0;

        var id = result.HardwareId.Trim();
        if (device.HardwareIds.Any(candidate =>
            string.Equals(candidate?.Trim(), id, StringComparison.OrdinalIgnoreCase)))
            return 2;

        return device.CompatibleIds.Any(candidate =>
            string.Equals(candidate?.Trim(), id, StringComparison.OrdinalIgnoreCase))
            ? 1 : 0;
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
            _ => "Nessuna corrispondenza Hardware ID/Compatible ID con il dispositivo selezionato."
        };
    }

    public static bool IsDeviceCandidate(DiscoveryResult result, DriverInfo device) =>
        MatchStrength(result, device) > 0;
}
