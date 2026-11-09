using System.Globalization;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Safe, local-only display name for Windows PnP devices. WMI can return
/// an empty Name or even the full PNPDeviceID; never substitute the latter
/// in UI or official-vendor handoffs because it contains instance/serial data.
/// This helper does NOT affect the original PnP identity used for matching.
/// </summary>
public static class PnpDeviceDisplayName
{
    public const int MaximumNameLength = 140;

    public static string Create(
        string? wmiName, string? pnpClass, string? pnpInstanceId = null)
    {
        if (IsOrdinaryFriendlyName(wmiName, pnpInstanceId))
            return wmiName!.Trim();

        // Only fixed, known Windows PnP class names influence the label.
        // Arbitrary classes, instance strings and manufacturer names are
        // never copied to a fallback display field.
        return pnpClass?.Trim().ToUpperInvariant() switch
        {
            "DISPLAY" => "Scheda video (nome non disponibile)",
            "NET" => "Scheda di rete (nome non disponibile)",
            "BLUETOOTH" => "Dispositivo Bluetooth (nome non disponibile)",
            "MEDIA" => "Dispositivo audio (nome non disponibile)",
            "SYSTEM" => "Dispositivo di sistema (nome non disponibile)",
            "SOFTWARECOMPONENT" => "Componente software (nome non disponibile)",
            "EXTENSION" => "Estensione driver (nome non disponibile)",
            _ => "Dispositivo Windows (nome non disponibile)"
        };
    }

    private static bool IsOrdinaryFriendlyName(
        string? raw, string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var value = raw.Trim();
        if (value.Length is < 2 or > MaximumNameLength ||
            (!string.IsNullOrWhiteSpace(instanceId) &&
             value.Equals(instanceId.Trim(),
                 StringComparison.OrdinalIgnoreCase)))
            return false;

        // Reject the Windows bus/instance separator, path/URL syntax and
        // raw bus vendor/model tokens even if WMI reported them as "Name".
        // Keep ordinary names such as "Intel(R) Killer Wi-Fi AX1675i"
        // and "NVIDIA GeForce RTX 4060 Laptop GPU" unchanged.
        if (value.Contains('\\') || value.StartsWith('/') ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains("VEN_", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("VID_", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("SUBSYS_", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("PID_", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("SERIAL_", StringComparison.OrdinalIgnoreCase) ||
            value.Any(ch => char.IsControl(ch) || char.IsSurrogate(ch) ||
                char.GetUnicodeCategory(ch) is UnicodeCategory.Format or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator))
            return false;

        return true;
    }
}
