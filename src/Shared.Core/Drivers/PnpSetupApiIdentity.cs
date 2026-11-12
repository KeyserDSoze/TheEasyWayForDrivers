using System.Text;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Bounded, fail-closed decoder for read-only Windows SetupAPI
/// SPDRP_HARDWAREID / SPDRP_COMPATIBLEIDS (REG_MULTI_SZ, UTF-16 LE).
/// Values never leave local inventory and never authorize installation.
/// </summary>
public static class PnpSetupApiIdentity
{
    public const uint RegMultiSz = 7;
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumIds = 128;
    private static readonly UnicodeEncoding StrictUtf16 =
        new(false, false, true);

    public sealed record Enrichment(
        IReadOnlyList<string> HardwareIds,
        IReadOnlyList<string> CompatibleIds,
        bool UsedSetupApi,
        bool UsedSignedDriver);

    public static IReadOnlyList<string> Decode(
        byte[]? buffer, uint registryType, uint validBytes)
    {
        if (buffer is null || registryType != RegMultiSz ||
            validBytes is < 4 or > MaximumBytes ||
            validBytes > buffer.Length ||
            (validBytes & 1) != 0)
            return [];

        var length = checked((int)validBytes);
        // A native REG_MULTI_SZ needs TWO final UTF-16 NUL characters.
        // Truncation must never turn an arbitrary fragment into an ID.
        if (buffer[length - 1] != 0 || buffer[length - 2] != 0 ||
            buffer[length - 3] != 0 || buffer[length - 4] != 0)
            return [];

        string decoded;
        try
        {
            decoded = StrictUtf16.GetString(buffer, 0, length);
        }
        catch (DecoderFallbackException)
        {
            return [];
        }

        if (decoded.Length < 2 ||
            decoded[^1] != '\0' || decoded[^2] != '\0')
            return [];

        var values = decoded[..^2].Split('\0');
        if (values.Length > MaximumIds ||
            values.Any(string.IsNullOrEmpty))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<string>(values.Length);
        foreach (var value in values)
        {
            if (!PnpIdentifierFallback.IsStrictSingleId(value))
                continue;
            if (seen.Add(value))
                found.Add(value);
        }
        return found;
    }

    /// <summary>
    /// Normalize the Win32_PnPEntity HardwareID / CompatibleID REG_MULTI_SZ
    /// values BEFORE checking whether SetupAPI native fallback is needed.
    /// A nonempty WMI array with only malformed entries is NOT evidence of
    /// usable identity. Never turn a PnP instance path or user path into a
    /// hardware ID. Over-budget arrays are rejected as a whole rather than
    /// silently marking a truncated prefix as exhaustive.
    /// </summary>
    public static IReadOnlyList<string> SanitizeWmiIds(
        IReadOnlyList<string> rawIds)
    {
        ArgumentNullException.ThrowIfNull(rawIds);
        if (rawIds.Count > MaximumIds)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new List<string>(rawIds.Count);
        foreach (var id in rawIds)
        {
            if (PnpIdentifierFallback.IsStrictSingleId(id) && seen.Add(id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// A nonempty WMI HardwareID list can still identify only the PCI
    /// class or vendor, without the VEN+DEV model required for device
    /// correlation. Recover a model only from the SAME native PnP instance.
    /// A complete WMI hardware model or another bus always wins.
    /// Multiple conflicting VEN tokens are ambiguous and fail closed.
    /// </summary>
    public static bool NeedsPciHardwareModelRecovery(
        IReadOnlyList<string> wmiHardware)
    {
        ArgumentNullException.ThrowIfNull(wmiHardware);
        var valid = SanitizeWmiIds(wmiHardware);
        if (valid.Count == 0)
            return false; // ordinary missing-ID path already handles this

        string? vendor = null;
        foreach (var id in valid)
        {
            if (!TryIncompletePciIdentity(id, out var currentVendor))
                return false;
            if (currentVendor is null)
                continue;
            if (vendor is not null &&
                !vendor.Equals(currentVendor, StringComparison.OrdinalIgnoreCase))
                return false;
            vendor = currentVendor;
        }
        return true;
    }

    /// <summary>
    /// WMI may expose a generic but nonempty ID on USB, NVIDIA/Realtek
    /// audio or DCH SWC, just as it does for PCI. It must not prevent a
    /// read-only same-instance SetupAPI request for a complete model.
    /// Native IDs are still filtered by bus/vendor/function in Merge.
    /// </summary>
    public static bool NeedsSpecificHardwareModelRecovery(
        IReadOnlyList<string> wmiHardware)
    {
        ArgumentNullException.ThrowIfNull(wmiHardware);
        return NeedsPciHardwareModelRecovery(wmiHardware) ||
            PnpNonPciModelRecovery.TryGetScope(
                SanitizeWmiIds(wmiHardware), out _);
    }

    /// <summary>
    /// An already complete, recognized PCI VEN+DEV from WMI may still be
    /// missing the notebook-specific SUBSYS needed by an OEM INF. Permit
    /// ONE optional, same-instance native lookup only if every WMI
    /// HardwareID is a strict variant of the same public PCI model,
    /// none carries a SUBSYS and no reported revisions contradict.
    /// WMI remains first/authoritative; no model is guessed from names.
    /// </summary>
    public static bool NeedsPciOemSubsystemRecovery(
        IReadOnlyList<string> wmiHardware)
    {
        ArgumentNullException.ThrowIfNull(wmiHardware);
        return TryPciOemScope(SanitizeWmiIds(wmiHardware), out _, out _);
    }

    private static string? PciRevision(string id)
    {
        var start = id.IndexOf("&REV_", StringComparison.OrdinalIgnoreCase);
        return start < 0 ? null : id.Substring(start + 5, 2).ToUpperInvariant();
    }

    private static bool TryPciOemScope(
        IReadOnlyList<string> ids, out string model, out string? revision)
    {
        model = string.Empty;
        revision = null;
        if (ids.Count is 0 or >= MaximumIds)
            return false;

        foreach (var id in ids)
        {
            if (!PnpIdentifierFallback.IsStrictSingleId(id) ||
                id.Contains("&SUBSYS_", StringComparison.OrdinalIgnoreCase))
                return false;
            var key = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id);
            if (key is null ||
                !key.StartsWith(@"PCI\VEN_", StringComparison.Ordinal) ||
                PnpKnownVendorIdentity.FromId(id) is null ||
                (model.Length > 0 &&
                 !string.Equals(key, model, StringComparison.OrdinalIgnoreCase)))
                return false;

            model = key;
            var currentRevision = PciRevision(id);
            if (currentRevision is null)
                continue;
            if (revision is not null && revision != currentRevision)
                return false;
            revision = currentRevision;
        }

        return model.Length > 0;
    }

    /// <summary>
    /// Preserve WMI VEN+DEV and append ONLY directly read SetupAPI Hardware
    /// IDs for that exact device which are SUBSYS-qualified and agree on
    /// every VEN+DEV, SUBSYS and explicit revision. Multiple distinct
    /// SUBSYS values or malformed/foreign native rows veto everything.
    /// The full Windows Hardware ID remains local and never becomes a URL.
    /// </summary>
    private static IReadOnlyList<string> ReadMatchingPciOemHardware(
        IReadOnlyList<string> nativeHardware, string publicModel,
        string? wmiRevision)
    {
        if (nativeHardware.Count is 0 or > MaximumIds)
            return [];

        string? subsystem = null;
        string? nativeRevision = null;
        var qualified = new List<string>();
        var prefix = publicModel + "&SUBSYS_";
        foreach (var id in nativeHardware)
        {
            if (!PnpIdentifierFallback.IsStrictSingleId(id) ||
                !string.Equals(
                    MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id),
                    publicModel, StringComparison.OrdinalIgnoreCase))
                return [];

            var rev = PciRevision(id);
            if (rev is not null)
            {
                if ((wmiRevision is not null && rev != wmiRevision) ||
                    (nativeRevision is not null && rev != nativeRevision))
                    return [];
                nativeRevision = rev;
            }

            if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // A validated same-model bare/REV/CC token is fine. A
                // SUBSYS in an unexpected position is not evidence to add.
                if (id.Contains("&SUBSYS_", StringComparison.OrdinalIgnoreCase))
                    return [];
                continue;
            }

            var code = id.Substring(prefix.Length, 8).ToUpperInvariant();
            if (subsystem is not null && subsystem != code)
                return [];
            subsystem = code;
            qualified.Add(id);
        }

        return qualified;
    }

    // A recovered optional native Compatible ID may only describe the
    // SAME PCI model or a genuine PCI class token; never borrow a foreign
    // vendor/device into an otherwise valid NVIDIA/Killer WMI identity.
    private static bool IsPciOemCompatible(string? id, string publicModel) =>
        PnpIdentifierFallback.IsStrictSingleId(id) &&
        (string.Equals(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id),
            publicModel, StringComparison.OrdinalIgnoreCase) ||
         (id!.StartsWith(@"PCI\CC_", StringComparison.OrdinalIgnoreCase) &&
          NeedsPciHardwareModelRecovery([id])));

    // Only these *exact* PCI class/vendor-only token shapes qualify.
    // Malformed, DEV-bearing, instance/path and non-PCI IDs cannot trigger
    // an override of Windows' authoritative HardwareID list.
    private static bool TryIncompletePciIdentity(string id, out string? vendor)
    {
        vendor = null;
        const string classPrefix = @"PCI\CC_";
        if (id.StartsWith(classPrefix, StringComparison.OrdinalIgnoreCase))
            return IsClassCode(id[classPrefix.Length..]);

        const string vendorPrefix = @"PCI\VEN_";
        if (!id.StartsWith(vendorPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = id[vendorPrefix.Length..];
        if (rest.Length < 4 || !rest[..4].All(Uri.IsHexDigit))
            return false;
        if (rest.Length > 4)
        {
            const string classSuffix = "&CC_";
            if (!rest[4..].StartsWith(classSuffix,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsClassCode(rest[(4 + classSuffix.Length)..]))
                return false;
        }

        vendor = rest[..4];
        return true;
    }

    private static bool IsClassCode(string value) =>
        value.Length is 4 or 6 && value.All(Uri.IsHexDigit);

    private static bool IsValidatedPciModel(string? id) =>
        PnpIdentifierFallback.IsStrictSingleId(id) &&
        MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id)?
            .StartsWith(@"PCI\VEN_", StringComparison.OrdinalIgnoreCase) == true;

    // Unlike a class-only WMI token, a complete PCI model identifies its
    // VEN. Several different VEN values in the SAME returned native
    // hardware list are ambiguous: never choose a vendor arbitrarily or
    // correlate one generic WMI node with both NVIDIA and Killer offers.
    private static string? GetPciVendor(string? id)
    {
        const string prefix = @"PCI\VEN_";
        if (id is null ||
            !id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            id.Length < prefix.Length + 4 ||
            !id.AsSpan(prefix.Length, 4).ToString().All(Uri.IsHexDigit) ||
            (id.Length > prefix.Length + 4 &&
             id[prefix.Length + 4] != '&'))
            return null;
        return id.Substring(prefix.Length, 4);
    }

    // CompatibleIDs are weaker than HardwareIDs, but still participate in
    // exact WUA correlation. A class-only PCI device cannot silently
    // inherit a foreign vendor or a different native bus through them.
    // Preserve genuine PCI class tokens and matching vendor models/hints.
    private static bool IsScopedPciCompatible(string? id, string? vendor)
    {
        if (!PnpIdentifierFallback.IsStrictSingleId(id))
            return false;
        const string pciClass = @"PCI\CC_";
        if (id!.StartsWith(pciClass, StringComparison.OrdinalIgnoreCase))
            return IsClassCode(id[pciClass.Length..]);

        var candidateVendor = GetPciVendor(id);
        if (vendor is null || candidateVendor is null ||
            !vendor.Equals(candidateVendor, StringComparison.OrdinalIgnoreCase))
            return false;

        return IsValidatedPciModel(id) ||
            TryIncompletePciIdentity(id, out var incompleteVendor) &&
            incompleteVendor is not null;
    }

    // Incomplete non-PCI WMI Hardware IDs constrain both recovered native
    // and SignedDriver CompatibleIDs. An unrelated bus/vendor/function
    // is not made trustworthy merely because its syntax is well-formed.
    // Generic class/usage compatible hints remain allowed only if their
    // own validated scope does not contradict the original WMI scope.
    private static bool IsScopedNonPciCompatible(
        string? id, PnpNonPciModelRecovery.Scope scope)
    {
        if (!PnpIdentifierFallback.IsStrictSingleId(id))
            return false;

        if (PnpNonPciModelRecovery.IsMatchingNativeModel(id, scope))
            return true;

        if (!PnpNonPciModelRecovery.TryGetScope([id!], out var compatible))
            return false;

        return string.Equals(compatible.Bus, scope.Bus,
                   StringComparison.OrdinalIgnoreCase) &&
               (scope.Vendor is null || compatible.Vendor is null ||
                string.Equals(scope.Vendor, compatible.Vendor,
                    StringComparison.OrdinalIgnoreCase)) &&
               (scope.Function is null || compatible.Function is null ||
                string.Equals(scope.Function, compatible.Function,
                    StringComparison.OrdinalIgnoreCase)) &&
               (scope.Revision is null || compatible.Revision is null ||
                string.Equals(scope.Revision, compatible.Revision,
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A missing WMI HardwareID list may leave only a generic vendor-bound
    /// SetupAPI identity (PCI VEN/CC, USB VID, HD Audio VEN, SWC VEN, etc.).
    /// Such a token must not suppress a validated full SignedDriver model
    /// joined to the SAME PnP instance. Never override complete native IDs,
    /// borrow across vendors/buses, or recover from class-only identities
    /// without an explicit vendor constraint.
    /// </summary>
    private static string? RecoverSignedFromNativeVendorHints(
        IReadOnlyList<string> nativeIds, string? signedHardware)
    {
        if (nativeIds.Count is 0 or >= MaximumIds)
            return null;

        if (NeedsPciHardwareModelRecovery(nativeIds))
        {
            // Every native PCI token must have the SAME explicit vendor.
            // A bare CC_ class cannot prove which GPU or radio it belongs to.
            string? vendor = null;
            foreach (var id in nativeIds)
            {
                if (!TryIncompletePciIdentity(id, out var current) ||
                    current is null)
                    return null;
                if (vendor is not null &&
                    !vendor.Equals(current, StringComparison.OrdinalIgnoreCase))
                    return null;
                vendor = current;
            }
            return vendor is not null &&
                   IsValidatedPciModel(signedHardware) &&
                   signedHardware!.StartsWith(@"PCI\VEN_" + vendor + "&DEV_",
                       StringComparison.OrdinalIgnoreCase)
                ? signedHardware : null;
        }

        return PnpNonPciModelRecovery.TryGetScope(nativeIds, out var scope) &&
               scope.Vendor is not null &&
               PnpNonPciModelRecovery.IsMatchingNativeModel(
                   signedHardware, scope)
            ? signedHardware : null;
    }

    /// <summary>
    /// Strict priority: complete Win32_PnPEntity model IDs win. Only
    /// missing identity or incomplete PCI class/vendor-only tokens may use
    /// same-instance native SetupAPI; require validated VEN+DEV and preserve
    /// the earlier WMI tokens behind the recovered hardware model.
    /// No native fallback may borrow another device's vendor/identity.
    /// </summary>
    public static Enrichment Merge(
        IReadOnlyList<string> wmiHardware,
        IReadOnlyList<string> wmiCompatible,
        IReadOnlyList<string> nativeHardware,
        IReadOnlyList<string> nativeCompatible,
        string? signedHardware,
        string? signedCompatible)
    {
        ArgumentNullException.ThrowIfNull(wmiHardware);
        ArgumentNullException.ThrowIfNull(wmiCompatible);
        ArgumentNullException.ThrowIfNull(nativeHardware);
        ArgumentNullException.ThrowIfNull(nativeCompatible);

        var validWmiHardware = SanitizeWmiIds(wmiHardware);
        var validWmiCompatible = SanitizeWmiIds(wmiCompatible);
        var incompletePci = NeedsPciHardwareModelRecovery(validWmiHardware);
        var incompleteOther = PnpNonPciModelRecovery.TryGetScope(
            validWmiHardware, out var otherScope);
        var incompleteModel = incompletePci || incompleteOther;
        var qualifyOemPci = !incompleteModel &&
            TryPciOemScope(validWmiHardware, out var oemPciModel,
                out var oemPciRevision);
        // The native read is optional: a WMI VEN+DEV model already
        // correlates generic WUA offers. Only its OEM SUBSYS is missing.
        var nativeOem = qualifyOemPci
            ? ReadMatchingPciOemHardware(
                nativeHardware, oemPciModel!, oemPciRevision)
            : [];
        // Any WMI VEN evidence constrains recovery to THAT vendor only.
        // This prevents a foreign native ID from silently relabeling the
        // device when WMI reports PCI\\VEN_10DE or VEN_8086&CC_0280.
        var wmiVendor = incompletePci
            ? validWmiHardware.Select(id =>
                TryIncompletePciIdentity(id, out var vendor) ? vendor : null)
                .FirstOrDefault(vendor => vendor is not null)
            : null;
        var nativeHw = validWmiHardware.Count == 0 || incompleteModel
            ? nativeHardware.Where(id => incompletePci
                    ? IsValidatedPciModel(id) &&
                      (wmiVendor is null || id!.StartsWith(
                          @"PCI\VEN_" + wmiVendor + "&DEV_",
                          StringComparison.OrdinalIgnoreCase))
                    : incompleteOther
                        ? PnpNonPciModelRecovery.IsMatchingNativeModel(
                            id, otherScope)
                        : PnpIdentifierFallback.IsStrictSingleId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];
        // With only WMI PCI class evidence, conflicting native VEN+DEV
        // values are NOT evidence of multiple devices or multiple drivers.
        // Reject the whole ambiguous native identity rather than choosing
        // whichever vendor happened to be first. Vendor-constrained WMI
        // already filters foreign models above.
        var ambiguousNativePci = incompletePci && wmiVendor is null &&
            nativeHw.Select(GetPciVendor)
                .Where(vendor => vendor is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1).Any();
        if (ambiguousNativePci)
            nativeHw = [];

        var scopedPciVendor = wmiVendor ?? nativeHw
            .Select(GetPciVendor)
            .FirstOrDefault(vendor => vendor is not null);

        // WMI lacks HardwareIDs; SetupAPI gave only vendor-specific generic
        // hints. A complete SignedDriver ID for THIS instance can restore
        // exact NVIDIA/Intel/Killer/audio identity. Scope every weaker
        // compatible ID too, so the recovery cannot import another vendor.
        var nativeOnlySigned = validWmiHardware.Count == 0
            ? RecoverSignedFromNativeVendorHints(nativeHw, signedHardware)
            : null;
        var nativeOnlyPci = nativeOnlySigned is not null &&
            nativeHw[0].StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase);
        var nativeOnlyOther = nativeOnlySigned is not null && !nativeOnlyPci &&
            PnpNonPciModelRecovery.TryGetScope(nativeHw, out _);
        var nativeOnlyScope = default(PnpNonPciModelRecovery.Scope);
        if (nativeOnlyOther)
            PnpNonPciModelRecovery.TryGetScope(nativeHw, out nativeOnlyScope);

        var nativeComp = validWmiCompatible.Count == 0
            ? nativeCompatible.Where(id =>
                    incompletePci || nativeOnlyPci
                        ? !ambiguousNativePci &&
                          IsScopedPciCompatible(id, scopedPciVendor)
                        : incompleteOther
                            ? IsScopedNonPciCompatible(id, otherScope)
                            : nativeOnlyOther
                                ? IsScopedNonPciCompatible(id, nativeOnlyScope)
                                : qualifyOemPci
                             ? IsPciOemCompatible(id, oemPciModel!)
                             : PnpIdentifierFallback.IsStrictSingleId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];

        // SignedDriver metadata is joined to this exact PnP DeviceID in the
        // WMI inventory. If SetupAPI has NO validated model, a single
        // specific signed ID may recover an incomplete WMI HardwareID.
        // Apply the SAME bus/vendor/function/type constraints as SetupAPI:
        // never borrow a model from a different PnP device or override a
        // complete WMI/native HardwareID.
        var signedSpecific = incompleteModel && !ambiguousNativePci &&
            nativeHw.Length == 0 &&
            (incompletePci
                ? IsValidatedPciModel(signedHardware) &&
                    (wmiVendor is null || signedHardware!.StartsWith(
                        @"PCI\VEN_" + wmiVendor + "&DEV_",
                        StringComparison.OrdinalIgnoreCase))
                : PnpNonPciModelRecovery.IsMatchingNativeModel(
                    signedHardware, otherScope))
            ? signedHardware : null;

        // Class/vendor-only tokens are NOT authoritative hardware models.
        // Keep original WMI evidence but prefer validated same-instance PCI
        // VEN+DEV for exact matching and vendor discovery; if no native
        // model is available, retain WMI identity unchanged.
        var preferredHardware = nativeOnlySigned is not null
            ? new[] { nativeOnlySigned }.Concat(nativeHw)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : incompleteModel && nativeHw.Length > 0
                ? nativeHw.Concat(validWmiHardware)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumIds).ToArray()
                : incompleteModel && signedSpecific is not null
                    ? new[] { signedSpecific }.Concat(validWmiHardware)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(MaximumIds).ToArray()
                    : qualifyOemPci && nativeOem.Count > 0
                        ? validWmiHardware.Concat(nativeOem)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(MaximumIds).ToArray()
                    : validWmiHardware.Count > 0 ? validWmiHardware : nativeHw;
        var preferredCompatible = validWmiCompatible.Count > 0
            ? validWmiCompatible : nativeComp;
        // A SignedDriver CompatibleID belongs to the same instance, but
        // must not reintroduce a foreign bus/vendor which the more
        // authoritative class-only PCI+SetupAPI evidence rejected.
        // WMI CompatibleIDs themselves always retain precedence.
        var scopedSignedCompatible = incompletePci || nativeOnlyPci
            ? !ambiguousNativePci &&
              IsScopedPciCompatible(signedCompatible,
                  scopedPciVendor ?? GetPciVendor(signedSpecific))
                ? signedCompatible : null
            : incompleteOther
                ? IsScopedNonPciCompatible(signedCompatible, otherScope)
                    ? signedCompatible : null
                : nativeOnlyOther
                    ? IsScopedNonPciCompatible(signedCompatible, nativeOnlyScope)
                        ? signedCompatible : null
                    : signedCompatible;
        var result = PnpIdentifierFallback.Combine(
            preferredHardware, preferredCompatible,
            signedHardware, scopedSignedCompatible);

        return new Enrichment(result.HardwareIds, result.CompatibleIds,
            nativeHw.Length > 0 || nativeOem.Count > 0 ||
                nativeComp.Length > 0,
            result.UsedSignedDriverFallback || signedSpecific is not null ||
            nativeOnlySigned is not null);
    }
}
