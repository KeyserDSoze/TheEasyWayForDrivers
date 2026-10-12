namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Reads identifiers supplied by Windows PnP. These are search hints,
/// never proof that a particular package can be installed safely.
/// </summary>
public static class HardwareIdentityParser
{
    public static string Describe(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
            return "Identificativo hardware non disponibile.";

        var value = hardwareId.Trim();
        var separator = value.IndexOf('\\');
        if (separator <= 0 || separator == value.Length - 1)
            return "Identificativo non riconosciuto: usa l'Hardware ID completo sul sito ufficiale.";

        var bus = value[..separator].ToUpperInvariant();
        var fields = value[(separator + 1)..].Split('&', '\\');
        var tokens = fields
            .Select(field => field.Split('\\')[0])
            .ToArray();

        if (bus == "PCI" &&
            TryField(tokens, "VEN_", 4, out var vendor) &&
            TryField(tokens, "DEV_", 4, out var device))
        {
            var sub = TryField(tokens, "SUBSYS_", 8, out var subsystem)
                ? $", sottosistema {subsystem}"
                : string.Empty;
            return $"Bus PCI · produttore {vendor} · dispositivo {device}{sub}. Verificare il modello prima di scaricare.";
        }

        if (bus == "USB" &&
            TryField(tokens, "VID_", 4, out var usbVendor) &&
            TryField(tokens, "PID_", 4, out var product))
            return $"Bus USB · produttore {usbVendor} · prodotto {product}. Verificare il modello prima di scaricare.";

        if (bus == "ACPI")
        {
            var code = tokens[0];
            if (code.Length is >= 4 and <= 24 && code.All(char.IsLetterOrDigit))
                return $"Dispositivo ACPI · identificativo {code.ToUpperInvariant()}. Verificare il modello del PC sul sito OEM.";
        }

        return "Identificativo non riconosciuto: usa l'Hardware ID completo sul sito ufficiale.";
    }

    private static bool TryField(IEnumerable<string> fields, string prefix, int length, out string code)
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
