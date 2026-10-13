namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Interprets official OEM catalog metadata only. Never establishes hardware
/// applicability, file trust, completeness or permission to install.
/// </summary>
public static class OemPackageAssessment
{
    public enum Kind { DeploymentPack, DriverMetadata, FirmwareOrBios, Unknown }
    public enum Disposition { ManualReview, ExcludeFromDriverFlow }

    public sealed record Result(string Vendor, string PackageId, Kind PackageKind,
        Disposition Action, string Reason, bool IsInstallable = false,
        bool SignatureVerified = false, bool DeviceMatchVerified = false);

    public static Result FromDell(DellDriverPackCatalog.Pack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return new Result("Dell", SafeLabel(pack.PackagePath),
            Kind.DeploymentPack, Disposition.ManualReview,
            "Catalogo deployment: il modello commerciale e il sistema operativo dichiarato non dimostrano un match Hardware ID, firma o integrità.");
    }

    public static Result FromHp(HpSoftpaqEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        // HP parser already restricts Category=Driver. Other CMSL categories
        // are not normalized to this record and must not be inferred by title.
        return new Result("HP", SafeLabel(item.Id),
            Kind.DriverMetadata, Disposition.ManualReview,
            "SoftPaq classificato Driver nel catalogo: non verificati INF/CAT, Hardware ID e dipendenze.");
    }

    public static Result FromLenovo(LenovoCatalogParser.Package item)
    {
        ArgumentNullException.ThrowIfNull(item);
        // PackageType 2 was enforced by LenovoCatalogParser. Category is only
        // descriptive metadata, not an executable deployment instruction.
        return new Result("Lenovo", SafeLabel(item.Id),
            Kind.DriverMetadata, Disposition.ManualReview,
            "PackageType 2: metadati driver OEM, applicabilità al dispositivo e firma non verificate.");
    }

    public static Result Unknown(string? vendor, string? packageId, bool explicitlyFirmware)
        => new(SafeLabel(vendor), SafeLabel(packageId),
            explicitlyFirmware ? Kind.FirmwareOrBios : Kind.Unknown,
            explicitlyFirmware ? Disposition.ExcludeFromDriverFlow : Disposition.ManualReview,
            explicitlyFirmware
                ? "BIOS/firmware: non instradare nel normale percorso driver."
                : "Tipo pacchetto OEM sconosciuto: nessuna valutazione di installabilità.");

    private static string SafeLabel(string? value)
    {
        var cleaned = new string((value ?? "").Where(c => !char.IsControl(c)).Take(160).ToArray()).Trim();
        return cleaned.Length == 0 ? "Non determinato" : cleaned;
    }
}
