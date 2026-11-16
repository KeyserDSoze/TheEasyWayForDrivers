using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// A discovery result is evidence about a potential source, not permission to
/// install a driver or proof that an OEM package matches an individual device.
/// </summary>
public enum DiscoveryResultKind
{
    WindowsUpdateDriver,
    DellDeploymentPack,
    HpSoftpaqDriver,
    LenovoCatalogDriver
}

public sealed record DiscoveryResult(
    DiscoveryResultKind Kind,
    string Source,
    string Identifier,
    string Title,
    string Version,
    string Evidence,
    string Guidance,
    string? HardwareId = null,
    // WUA Type=Driver can reference multiple devices via driver-update
    // entries; retain their bounded IDs for the Discovery+ filter.
    IReadOnlyList<string>? AdditionalHardwareIds = null,
    bool WuaIdsIncomplete = false)
{
    public string VerificationLabel => Kind == DiscoveryResultKind.WindowsUpdateDriver
        ? "Candidato WUA · verifica richiesta"
        : "Solo catalogo · non installabile";

    public string KindLabel => Kind switch
    {
        DiscoveryResultKind.WindowsUpdateDriver => "Driver Windows Update",
        DiscoveryResultKind.DellDeploymentPack => "Pacchetto deployment Dell",
        DiscoveryResultKind.HpSoftpaqDriver => "SoftPaq HP",
        DiscoveryResultKind.LenovoCatalogDriver => "Catalogo Lenovo",
        _ => "Sorgente non identificata"
    };
}

public static class DiscoveryResultAdapter
{
    public static IReadOnlyList<DiscoveryResult> FromWindowsUpdate(
        IEnumerable<DriverUpdateInfo> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        // The UI passes already-confirmed COM results. A public adapter may
        // also be called with legacy IPC rows, however: never label an OEM
        // source or a non-GUID identity as "Windows Update" (#536).
        // This provenance filter is NOT evidence of a Completed pass.
        return updates
            .Where(WindowsDriverCandidateMerge.IsKnownWuaGuid)
            .Select(ToSafeWindowsUpdateResult)
            .ToArray();
    }

    private static DiscoveryResult ToSafeWindowsUpdateResult(
        DriverUpdateInfo update)
    {
        // The raw WUA entry can contain an instance path or an untrusted
        // Unicode token. Preserve only strict single model IDs for
        // Discovery+ display and its existing exact per-device matching.
        var primary = SafeHardwareId(update.HardwareId);
        var hasInvalid = !string.IsNullOrEmpty(update.HardwareId) &&
            primary is null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (primary is not null)
            seen.Add(primary);

        var additional = new List<string>();
        var rawExtras = update.AdditionalHardwareIds;
        var overLimit = (rawExtras?.Count ?? 0) >
            WuaDriverEntryLimits.MaximumAdditionalIds;
        foreach (var raw in (rawExtras ?? [])
            .Take(WuaDriverEntryLimits.MaximumAdditionalIds))
        {
            var id = SafeHardwareId(raw);
            if (id is null)
            {
                hasInvalid = true;
                continue;
            }
            if (seen.Add(id))
                additional.Add(id);
        }

        var incomplete = update.AdditionalHardwareIdsIncomplete ||
            overLimit || hasInvalid;
        // Treat all presentation strings as untrusted even when a real WUA
        // source provided the GUID. Never change the original WUA update or
        // its install eligibility: this result is read-only.
        var title = WuaReadOnlyTitlePolicy.IsSafe(update.Title)
            ? update.Title
            : "Titolo driver non verificabile";
        var version = SafeVersion(update.Version);

        return new DiscoveryResult(
            DiscoveryResultKind.WindowsUpdateDriver,
            "Windows Update",
            Guid.Parse(update.Id).ToString("D"),
            title,
            version,
            DescribeWuaAssociation(primary, additional.Count, incomplete),
            "Candidato Windows Update. Verificare dispositivo, versione Windows e corrispondenza ID prima dell'installazione.",
            primary,
            additional,
            incomplete);
    }

    private static string? SafeHardwareId(string? raw) =>
        DriverUpdateMatcher.IsCorrelatableHardwareId(raw)
            ? raw!.Trim(' ')
            : null;

    private static string SafeVersion(string? version)
    {
        // Only one compact version token is useful in this read-only UI.
        // A local path, email, error line or private order/serial token
        // cannot be a verifiable Windows driver-version display string.
        if (string.IsNullOrEmpty(version) ||
            version.Length > 64 || !char.IsAsciiDigit(version[0]) ||
            version.Any(ch => !(char.IsAsciiLetterOrDigit(ch) ||
                ch is '.' or '-' or '+' or '_')) ||
            new[] { "PRIVATE_", "SECRET_", "ORDER_", "CUSTOMER_",
                    "SERIAL_", "SERIALNUMBER", "INSTANCEID" }.Any(marker =>
                version.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return "N/D";
        return version;
    }

    private static string DescribeWuaAssociation(
        string? primaryId, int additionalCount, bool incomplete)
    {
        var primary = primaryId is null
            ? additionalCount > 0
                ? "ID principale non verificato; associazioni aggiuntive WUA validate"
                : "Hardware ID WUA non verificabile"
            : $"ID dichiarato: {primaryId}";
        var suffix = additionalCount > 0
            ? $"; {additionalCount} {(additionalCount == 1
                ? "ID aggiuntivo validato" : "ID aggiuntivi validati")} dallo stesso aggiornamento"
            : "";
        return primary + suffix + (incomplete
            ? "; elenco associazioni WUA incompleto, assenza non verificabile."
            : ".");
    }

    public static IReadOnlyList<DiscoveryResult> FromDell(
        IEnumerable<DellDriverPackCatalog.Pack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        return packs.Select(raw =>
        {
            // #460: direct public Pack values can bypass the official
            // XML parser. Never copy a raw path, serial, OS or model
            // into Discovery+ without independent revalidation.
            var pack = DellDriverPackCatalog.ValidatePack(raw);
            return new DiscoveryResult(
                DiscoveryResultKind.DellDeploymentPack,
                "Dell Command Deploy",
                pack.PackagePath,
                $"Driver Pack per {pack.Model}",
                pack.Release,
                $"Modello catalogo: {pack.Model}; sistemi: {pack.OperatingSystems}",
                "Pacchetto deployment, NON driver singolo. OS, piattaforma e firma da verificare; installazione OEM non disponibile.");
        }).ToArray();
    }

    public static IReadOnlyList<DiscoveryResult> FromLenovo(
        IEnumerable<LenovoCatalogParser.Package> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return packages.Select(raw =>
        {
            // Package can come directly from legacy IPC/session data; the
            // parser's identity and privacy checks cannot be assumed.
            var package = LenovoCatalogParser.ValidatePackage(raw);
            return new DiscoveryResult(
                DiscoveryResultKind.LenovoCatalogDriver,
                "Lenovo.Client.Scripting",
                package.Id,
                package.Name,
                package.Version.Length == 0 ? "N/D" : package.Version,
                $"Categoria Lenovo: {package.Category}; data: {package.ReleaseDate}",
                "Metadati Lenovo per MachineType indicato: nessun Hardware ID per dispositivo verificato. Pacchetto non installabile da OmegaDrive.");
        }).ToArray();
    }

    public static IReadOnlyList<DiscoveryResult> FromHp(
        IEnumerable<HpSoftpaqEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(raw =>
        {
            // #459: public SoftPaq entries may also arrive from legacy
            // sessions/callers without passing through the JSON parser.
            var entry = HpSoftpaqCatalogParser.ValidateEntry(raw);
            return new DiscoveryResult(
                DiscoveryResultKind.HpSoftpaqDriver,
                "HP CMSL",
                entry.Id,
                entry.Title,
                entry.Version,
                $"Tipo: {entry.ReleaseType}; data: {entry.PublishedDate}",
                "Metadati HP CMSL per la piattaforma selezionata; compatibilità per singolo dispositivo non verificata. Installazione OEM non disponibile.");
        }).ToArray();
    }
}
