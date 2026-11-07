using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>Potential equality evidence only, never SetupAPI applicability.</summary>
public static class OemHardwareEvidence
{
    public enum Strength { None, CompatibleId, HardwareId }
    public sealed record Match(string InfPath, string Section, string DeclaredId,
        string DeviceId, string DeviceName, Strength Level);
    public sealed record Report(IReadOnlyList<Match> Matches, int DevicesExamined)
    {
        public bool IsInstallable => false;
        public bool IsVerifiedCompatible => false;
        public bool UsedFullTargetedMatching { get; init; }
        public bool HasUnexaminedInf { get; init; }
    }

    public static Report Compare(OemZipInfEvidence.Report evidence, IEnumerable<DriverInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(devices);
        var available = devices.Take(2049).ToArray();
        if (available.Length > 2048)
            throw new InvalidDataException("Troppi dispositivi per il confronto OEM.");
        var matches = new List<Match>();
        foreach (var device in available)
        {
            var hardware = new HashSet<string>(
                device.HardwareIds.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
            var compatible = new HashSet<string>(
                device.CompatibleIds.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var inf in evidence.InfFiles.Where(inf => inf.IsReadable))
            {
                var ids = evidence.HasTargetedDeviceMatches
                    ? evidence.TargetedMatches.TryGetValue(inf.Path, out var specific)
                        ? specific : []
                    : inf.DeclaredIds;
                foreach (var id in ids)
                {
                // An INF's SECOND and later Models IDs are compatible
                // aliases, even if WMI reports one as HardwareId. Never
                // call such evidence an exact primary-hardware match.
                var strength = id.IsPrimaryHardwareId &&
                    hardware.Contains(id.HardwareId)
                    ? Strength.HardwareId
                    : hardware.Contains(id.HardwareId) ||
                      compatible.Contains(id.HardwareId)
                        ? Strength.CompatibleId : Strength.None;
                if (strength == Strength.None) continue;
                if (matches.Count >= 500)
                    throw new InvalidDataException("Troppi potenziali match OEM.");
                matches.Add(new Match(inf.Path, id.Section, id.HardwareId,
                    device.DeviceId, device.Name, strength));
                }
            }
        }
        return new Report(matches.OrderByDescending(x => x.Level)
            .ThenBy(x => x.InfPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray(),
            available.Length)
        {
            UsedFullTargetedMatching = evidence.HasTargetedDeviceMatches,
            HasUnexaminedInf = evidence.RejectedInfCount > 0
        };
    }
}
