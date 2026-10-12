using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// A discovery result is evidence about a potential source, not permission to
/// install a driver or proof that an OEM package matches an individual device.
/// </summary>
public enum DiscoveryResultKind
{
    WindowsUpdateDriver,
    DellDeploymentPack,
    HpSoftpaqDriver
}

public sealed record DiscoveryResult(
    DiscoveryResultKind Kind,
    string Source,
    string Identifier,
    string Title,
    string Version,
    string Evidence,
    string Guidance)
{
    public string KindLabel => Kind switch
    {
        DiscoveryResultKind.WindowsUpdateDriver => "Driver Windows Update",
        DiscoveryResultKind.DellDeploymentPack => "Pacchetto deployment Dell",
        DiscoveryResultKind.HpSoftpaqDriver => "SoftPaq HP",
        _ => "Sorgente non identificata"
    };
}

public static class DiscoveryResultAdapter
{
    public static IReadOnlyList<DiscoveryResult> FromWindowsUpdate(
        IEnumerable<DriverUpdateInfo> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        return updates.Select(update => new DiscoveryResult(
            DiscoveryResultKind.WindowsUpdateDriver,
            "Windows Update",
            update.Id,
            update.Title,
            update.Version ?? "N/D",
            string.IsNullOrWhiteSpace(update.HardwareId)
                ? "Il catalogo non ha riportato un Hardware ID."
                : $"ID dichiarato: {update.HardwareId}",
            "Candidato Windows Update. Verificare dispositivo, versione Windows e corrispondenza ID prima dell'installazione."
        )).ToArray();
    }

    public static IReadOnlyList<DiscoveryResult> FromDell(
        IEnumerable<DellDriverPackCatalog.Pack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        return packs.Select(pack => new DiscoveryResult(
            DiscoveryResultKind.DellDeploymentPack,
            "Dell Command Deploy",
            pack.PackagePath,
            $"Driver Pack per {pack.Model}",
            pack.Release,
            $"Modello catalogo: {pack.Model}; sistemi: {pack.OperatingSystems}",
            "Pacchetto deployment, NON driver singolo. OS, piattaforma e firma da verificare; installazione OEM non disponibile."
        )).ToArray();
    }

    public static IReadOnlyList<DiscoveryResult> FromHp(
        IEnumerable<HpSoftpaqEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(entry => new DiscoveryResult(
            DiscoveryResultKind.HpSoftpaqDriver,
            "HP CMSL",
            entry.Id,
            entry.Title,
            entry.Version,
            $"Tipo: {entry.ReleaseType}; data: {entry.PublishedDate}",
            "Metadati HP CMSL per la piattaforma selezionata; compatibilità per singolo dispositivo non verificata. Installazione OEM non disponibile."
        )).ToArray();
    }
}
