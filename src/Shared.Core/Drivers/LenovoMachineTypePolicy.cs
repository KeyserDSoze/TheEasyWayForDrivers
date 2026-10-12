namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// The Lenovo Client Scripting Module requires a four-character machine type,
/// not a serial number, free-text PC model or a user-supplied PowerShell command.
/// </summary>
public static class LenovoMachineTypePolicy
{
    public static bool TryNormalize(string? raw, out string machineType)
    {
        machineType = string.Empty;
        if (raw is null)
            return false;
        var value = raw.Trim();
        if (value.Length != 4 || !value.All(c =>
                c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            return false;
        machineType = value.ToUpperInvariant();
        return true;
    }
}
