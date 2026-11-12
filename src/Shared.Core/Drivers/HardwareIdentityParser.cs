namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Describes public PnP models for the local Windows device detail.
/// This is a user-facing HINT, not a raw ID dump or evidence that a
/// particular OEM/Windows Update driver can be installed.
/// </summary>
public static class HardwareIdentityParser
{
    private const string Unknown =
        "Identificativo non riconosciuto: usa l'Hardware ID completo sul sito ufficiale.";

    public static string Describe(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
            return "Identificativo hardware non disponibile.";

        var value = hardwareId.Trim();

        // A Hardware ID is one bus plus ONE public device-model component,
        // not a PnP instance (bus\model\private serial), UNC path, URL or
        // arbitrary IPC text. Never copy part of an instance into a hint.
        if (value.Length > 512 || value.Count(ch => ch == '\\') != 1 ||
            value.Any(ch => char.IsControl(ch) || char.IsSurrogate(ch) ||
                ch is '/' or ':' or '\u202A' or '\u202B' or '\u202C' or
                      '\u202D' or '\u202E' or '\u2066' or '\u2067' or
                      '\u2068' or '\u2069'))
            return Unknown;

        var separator = value.IndexOf('\\');
        if (separator <= 0 || separator == value.Length - 1)
            return Unknown;

        var bus = value[..separator].ToUpperInvariant();
        var identifier = value[(separator + 1)..];
        if (bus.Length is < 2 or > 32 ||
            !bus.All(char.IsAsciiLetterOrDigit))
            return Unknown;

        if (bus is "PCI" or "USB")
        {
            // Only an entire, validated public model may supply VEN/DEV
            // or VID/PID details. Previous token search could accidentally
            // accept VEN_/DEV_ inside a malformed field or instance.
            if (MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value) is null)
                return Unknown;

            var tokens = identifier.Split('&');
            if (bus == "PCI" &&
                TryField(tokens, "VEN_", 4, out var vendor) &&
                TryField(tokens, "DEV_", 4, out var device))
            {
                var sub = TryField(tokens, "SUBSYS_", 8, out var subsystem)
                    ? $", sottosistema {subsystem}"
                    : string.Empty;
                return $"Bus PCI · produttore {vendor} · dispositivo {device}{sub}. " +
                    "Verificare il modello prima di scaricare.";
            }

            if (bus == "USB" &&
                TryField(tokens, "VID_", 4, out var usbVendor) &&
                TryField(tokens, "PID_", 4, out var product))
                return $"Bus USB · produttore {usbVendor} · prodotto {product}. " +
                    "Verificare il modello prima di scaricare.";

            return Unknown;
        }

        if (bus == "ACPI")
        {
            // These are recognized PUBLIC model code families. An
            // arbitrary alphanumeric ACPI identifier may instead be
            // device-specific: show its bus without echoing its contents.
            var publicPnp = identifier.Length == 7 &&
                identifier.StartsWith("PNP", StringComparison.OrdinalIgnoreCase) &&
                identifier.AsSpan(3).ToArray().All(char.IsAsciiHexDigit);
            var publicNvidia = MicrosoftUpdateCatalogSearch
                .TryGetPublicModelKey(value) is { } publicModel &&
                publicModel.StartsWith(@"ACPI\NVDA", StringComparison.Ordinal);
            if (publicPnp || publicNvidia)
                return $"Dispositivo ACPI · identificativo {identifier.ToUpperInvariant()}. " +
                    "Verificare il modello del PC sul sito OEM.";
        }

        var type = bus switch
        {
            "ACPI" => "dispositivo ACPI",
            "HID" => "periferica HID (mouse, tastiera o controllo)",
            "HDAUDIO" => "dispositivo audio HD",
            "INTELAUDIO" => "dispositivo audio su bus Intel Smart Sound",
            "SOUNDWIRE" or "SNDW" => "dispositivo audio SoundWire",
            "ACP" => "controller audio ACP (produttore non verificato)",
            "SWC" => "componente software PnP (DCH)",
            "BTHENUM" or "BTHLEDEVICE" or "BTH" => "dispositivo Bluetooth",
            "USBSTOR" or "STORAGE" or "SCSI" or "SD" or "IDE" => "dispositivo di archiviazione",
            "MONITOR" or "DISPLAY" => "monitor o display",
            "USBPRINT" or "LPTENUM" or "DOT4PRT" or
                "BTHPRINT" or "WSDPRINT" or "1394" =>
                    "periferica di stampa (protocollo Windows)",
            "SERENUM" => "periferica seriale",
            "VMBUS" => "dispositivo virtuale Hyper-V",
            "ROOT" or "SWD" => "dispositivo virtuale o software",
            "I2C" or "PCMCIA" or "ISAPNP" or "UEFI" or "UMB" or "MEDIA" =>
                "dispositivo PnP",
            _ => null
        };

        // For other buses, a raw identifier is not necessarily a public
        // model. Even a syntactically valid ROOT\PRIVATE_SERIAL must
        // NEVER be echoed into a screenshot or a support summary.
        if (type is not null &&
            identifier.Length is >= 2 and <= 150 &&
            identifier.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '_' or '&' or '{' or '}' or '+' or '#' or '-'))
            return $"Identificato bus {bus} · {type}. " +
                "Identificativo specifico non riportato; " +
                "controllare il modello sul sito ufficiale, " +
                "senza dedurne il produttore.";

        return Unknown;
    }

    private static bool TryField(IEnumerable<string> fields, string prefix, int length,
        out string code)
    {
        foreach (var field in fields)
        {
            if (field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = field[prefix.Length..];
                if (candidate.Length == length && candidate.All(Uri.IsHexDigit))
                {
                    code = candidate.ToUpperInvariant();
                    return true;
                }
            }
        }
        code = string.Empty;
        return false;
    }
}
