namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>Normalizes untrusted WMI system identity values without guessing a model.</summary>
public static class SystemIdentityNormalizer
{
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim();
        if (cleaned.Length > 160)
            return null;

        var placeholders = new[]
        {
            "To be filled by O.E.M.", "To Be Filled By O.E.M.",
            "System Product Name", "System Manufacturer",
            "Default string", "Not Applicable", "Not Available",
            "Unknown", "None", "N/A", "OEM"
        };

        if (placeholders.Any(placeholder =>
            string.Equals(cleaned, placeholder, StringComparison.OrdinalIgnoreCase)))
            return null;

        return cleaned;
    }

    public static string BuildSupportInstructions(string? manufacturer, string? model)
    {
        if (model is not null)
            return $"Sul portale ufficiale cerca il modello esatto «{model}» e verifica sistema operativo e Hardware ID prima del download.";

        return manufacturer is not null
            ? "Il modello del PC non è disponibile: identificalo sul sito del produttore prima di selezionare un driver."
            : "Produttore e modello non identificati: controlla le informazioni di sistema Windows e usa solo supporto ufficiale verificato.";
    }
}
