namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>Normalizes untrusted WMI system identity values without guessing a model.</summary>
public static class SystemIdentityNormalizer
{
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // #475: validate the raw WMI/SMBIOS field before Trim() could
        // repair a forged "Dell\\n" or "HP\\t" identity into an
        // apparently trusted manufacturer. Also keep local paths, PnP IDs
        // and private references out of OEM support text/logging.
        if (value.Length > 160 || value.Contains('\\') ||
            value.Contains('@') ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/mnt/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith('/') ||
            value.Any(ch => char.IsControl(ch) || char.IsSurrogate(ch) ||
                (char.IsWhiteSpace(ch) && ch != ' ') ||
                char.GetUnicodeCategory(ch) is
                    System.Globalization.UnicodeCategory.Format or
                    System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator))
            return null;

        foreach (var marker in new[]
        {
            "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_",
            "INSTANCEID", "SERIALNUMBER"
        })
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return null;

        for (var i = 0; i + 2 < value.Length; i++)
            if (char.IsAsciiLetter(value[i]) && value[i + 1] == ':' &&
                value[i + 2] == '/')
                return null;

        var cleaned = value.Trim(' ');
        if (cleaned.Length > 160)
            return null;

        var placeholders = new[]
        {
            "To be filled by O.E.M.", "To Be Filled By O.E.M.",
            "System Product Name", "System Manufacturer",
            "Default string", "Not Applicable", "Not Available",
            "Unknown", "None", "N/A", "OEM",
            "All Series", "System Version", "Not Specified"
        };

        if (placeholders.Any(placeholder =>
            string.Equals(cleaned, placeholder, StringComparison.OrdinalIgnoreCase)))
            return null;

        return cleaned;
    }

    public static string BuildSupportInstructions(string? manufacturer, string? model)
    {
        if (model is not null)
            return $"Sul portale ufficiale cerca il modello esatto «{model}» e verifica sistema operativo, Hardware ID e versione del driver prima del download. Non installare un pacchetto solo perché il produttore coincide.";

        return manufacturer is not null
            ? "Il modello del PC non è disponibile: identificalo sul sito del produttore prima di selezionare un driver."
            : "Produttore e modello non identificati: controlla le informazioni di sistema Windows e usa solo supporto ufficiale verificato.";
    }
}
