using System.Text.Json;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Shared strict JSON metadata boundary for local OEM companion responses.
/// JsonElement.TryGetProperty chooses a value when a property occurs more
/// than once. That is unsafe for driver type, applicability and identity.
/// Reject the ambiguous object before reading ANY of those fields, while
/// leaving independent, unambiguous response entries intact.
/// </summary>
internal static class OemJsonMetadataIntegrity
{
    internal static bool HasDuplicateCriticalKeys(
        JsonElement entry, IReadOnlySet<string> criticalKeys)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return false;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in entry.EnumerateObject())
        {
            if (criticalKeys.Contains(property.Name) &&
                !seen.Add(property.Name))
                return true;
        }
        return false;
    }
}
