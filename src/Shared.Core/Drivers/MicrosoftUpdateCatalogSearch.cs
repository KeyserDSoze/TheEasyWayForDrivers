using System.Text.RegularExpressions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Explicit, browser-only handoff to Microsoft's public update catalog.
/// Windows Update Agent and the Catalog are different discovery surfaces.
/// A catalog search is never proof that a driver is applicable or installable.
/// No HTTP request is made here, and no PnP instance or private device ID is
/// included in a search URL.
/// </summary>
public static class MicrosoftUpdateCatalogSearch
{
    public const string CatalogHost = "www.catalog.update.microsoft.com";
    private const string SearchBase =
        "https://www.catalog.update.microsoft.com/Search.aspx?q=";
    private const int MaximumInputLength = 512;

    public sealed record Target(string Query, Uri Url, bool FromCompatibleId)
    {
        public bool IsInstallable => false;
        public bool CatalogWasQueried => false;
        public string Explanation =>
            "Ricerca manuale nel Microsoft Update Catalog per " +
            (Query.StartsWith(@"SOUNDWIRE\AGGREGAT", StringComparison.OrdinalIgnoreCase)
                ? "categoria audio SoundWire generica "
                : "modello hardware ") +
            (FromCompatibleId ? "(ID compatibile, evidenza più debole)" :
                "(Hardware ID)") +
            ". La ricerca non verifica versione, firma CAT, OEM, SUBSYS, " +
            "ranking Windows o installabilità.";
    }

    // Extract only vendor and model tokens. Do NOT transmit PnP instance
    // identifiers, SUBSYS, serials, OS build, device title, or
    // REV/MI/IG/COL HID interface fields.
    // The generic query can return unrelated OEM models: manual verification
    // is mandatory before even considering a download.
    private static readonly (Regex Pattern, Func<Match, string> Build)[] Formats =
    [
        (Pattern(@"PCI\\VEN_(?<v>[0-9A-F]{4})&DEV_(?<d>[0-9A-F]{4})(?:&(?:SUBSYS_[0-9A-F]{8}|REV_[0-9A-F]{2}|CC_[0-9A-F]{4,6}))*"),
            m => $@"PCI\VEN_{m.Groups["v"].Value}&DEV_{m.Groups["d"].Value}"),
        (Pattern(@"USB\\VID_(?<v>[0-9A-F]{4})&PID_(?<d>[0-9A-F]{4})(?:&(?:REV_[0-9A-F]{4}|MI_[0-9A-F]{2}))*"),
            m => $@"USB\VID_{m.Groups["v"].Value}&PID_{m.Groups["d"].Value}"),
        // HID peripherals (Logitech/Razer/etc.) publish VID+PID models.
        // Windows may append revision, interface, input-group or collection
        // tokens. These can distinguish HID interfaces locally, but a
        // PUBLIC browser-only Catalog search needs only VID+PID.
        // Never include PnP instance IDs or usage-only HID class tokens.
        (Pattern(@"HID\\VID_(?<v>[0-9A-F]{4})&PID_(?<d>[0-9A-F]{4})(?:&(?:REV_[0-9A-F]{4}|MI_[0-9A-F]{2}|IG_[0-9A-F]{2}|COL[0-9A-F]{2}))*"),
            m => $@"HID\VID_{m.Groups["v"].Value}&PID_{m.Groups["d"].Value}"),
        (Pattern(@"HDAUDIO\\FUNC_(?<f>[0-9A-F]{2})&VEN_(?<v>[0-9A-F]{4})&DEV_(?<d>[0-9A-F]{4})(?:&(?:SUBSYS_[0-9A-F]{8}|REV_[0-9A-F]{2}))*"),
            m => $@"HDAUDIO\FUNC_{m.Groups["f"].Value}&VEN_{m.Groups["v"].Value}&DEV_{m.Groups["d"].Value}"),
        (Pattern(@"INTELAUDIO\\FUNC_(?<f>[0-9A-F]{2})&VEN_(?<v>[0-9A-F]{4})&DEV_(?<d>[0-9A-F]{4})(?:&(?:SUBSYS_[0-9A-F]{8}|REV_[0-9A-F]{2}))*"),
            m => $@"INTELAUDIO\FUNC_{m.Groups["f"].Value}&VEN_{m.Groups["v"].Value}&DEV_{m.Groups["d"].Value}"),
        (Pattern(@"SWC\\VEN_(?<v>[0-9A-F]{4})&DEV_(?<d>[0-9A-F]{4})(?:&(?:SUBSYS_[0-9A-F]{8}|REV_[0-9A-F]{2}))*"),
            m => $@"SWC\VEN_{m.Groups["v"].Value}&DEV_{m.Groups["d"].Value}"),
        // ACPI notebook platform HID: an explicit REV_01/REV_0001
        // narrows the local device identity, while manual Catalog search
        // uses the public NVDA model. No UID, serial or arbitrary suffix.
        (Pattern(@"ACPI\\(?<v>NVDA)(?<d>[0-9A-F]{4})(?:&REV_(?:[0-9A-F]{2}|[0-9A-F]{4}))?"),
            m => $@"ACPI\{m.Groups["v"].Value}{m.Groups["d"].Value}"),
        // Public SoundWire aggregate IDs are generic bus class hints, not
        // proof that a particular codec or OEM package fits this computer.
        (Pattern(@"SOUNDWIRE\\(?<a>AGGREGATOR|AGGREGATEDSPEAKER)"),
            m => $@"SOUNDWIRE\{m.Groups["a"].Value}"),
        // Drop address, link, UID and OEM subsystem before opening Microsoft.
        // SDCA/MAN/FUNC are the non-instance model family for an explicit,
        // user-confirmed, browser-only search; no automatic catalog query.
        (Pattern(@"SOUNDWIRE\\SDCA_(?<s>[0-9A-F]{2})&MAN_(?<v>[0-9A-F]{4})&FUNC_(?<f>[0-9A-F]{4})(?:&TYPE_[0-9A-F]{2}&VER_[0-9A-F]{2}&ADR_[0-9A-F]{2}&LID_[0-9A-F]{2}&UID_[0-9A-F]{2}(?:&SUBSYS_[0-9A-F]{8})?)?"),
            m => $@"SOUNDWIRE\SDCA_{m.Groups["s"].Value}&MAN_{m.Groups["v"].Value}&FUNC_{m.Groups["f"].Value}")
    ];

    private static Regex Pattern(string body) =>
        new(@"\A" + body + @"\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

    public static Target? Create(DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        foreach (var id in device.HardwareIds)
        {
            var query = TryModelQuery(id);
            if (query is not null)
                return Build(query, fromCompatibleId: false);
        }

        foreach (var id in device.CompatibleIds)
        {
            var query = TryModelQuery(id);
            if (query is not null)
                return Build(query, fromCompatibleId: true);
        }

        return null;
    }

    private static Target Build(string query, bool fromCompatibleId)
    {
        var url = new Uri(SearchBase + Uri.EscapeDataString(query),
            UriKind.Absolute);
        return new Target(query, url, fromCompatibleId);
    }

    /// <summary>
    /// Returns a strictly validated, non-instance public model key, or null.
    /// This is also reused by the weaker read-only WUA review correlation.
    /// A stripped SUBSYS alone is NEVER authority to install a driver.
    /// </summary>
    public static string? TryGetPublicModelKey(string? raw) => TryModelQuery(raw);

    private static string? TryModelQuery(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Length > MaximumInputLength ||
            raw.Any(char.IsControl) ||
            raw.Any(c => !char.IsAsciiLetterOrDigit(c) &&
                c is not ('\\' or '&' or '_')) ||
            raw.Count(c => c == '\\') != 1)
            return null;

        // A precise product grammar, not the generic HID_DEVICE,
        // VID-only or Windows HID usage-page identity, is required
        // BEFORE producing a URL. Reuse the WUA/INF HID safety parser;
        // this is still only a user-triggered, review-only handoff.
        if (raw.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) &&
            !HidPnpMatchPolicy.IsSpecificHardwareId(raw))
            return null;

        foreach (var (pattern, build) in Formats)
        {
            var match = pattern.Match(raw);
            if (match.Success)
                return build(match).ToUpperInvariant();
        }

        return null;
    }
}
