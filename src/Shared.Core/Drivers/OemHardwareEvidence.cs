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

        // #560: raw WMI/SignedDriver snapshots may contain multiple rows
        // for one Windows PnP instance. Only reconciled identities can
        // attest an INF/device association. Counts never contain IDs.
        public int TrustedDevices { get; init; }
        public int ExcludedUnverifiedPnpIdentities { get; init; }
    }

    public static Report Compare(OemZipInfEvidence.Report evidence, IEnumerable<DriverInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(devices);
        var available = devices.Take(2049).ToArray();
        if (available.Length > 2048)
            throw new InvalidDataException("Troppi dispositivi per il confronto OEM.");
        // The ZIP inspector preview used to accept contradictory *same*
        // DeviceId rows as separate exact INF matches. Apply the SAME
        // shared PnP-ID budget and normalized-instance/model consensus
        // as WUA and full local OEM comparison, before indexing IDs.
        if (ReferenceDriverCoverage.ExceedsDiagnosticBudget(available, null))
            throw new InvalidDataException(
                "Inventario PnP oltre il limite di confronto OEM.");

        var trusted = PnpDistinctTrustedDevices.Create(available);
        var uniqueIdentities = available
            .Where(device => device is not null &&
                !string.IsNullOrWhiteSpace(device.DeviceId))
            .Select(device => PnpDistinctTrustedDevices.NormalizeInstanceKey(
                device.DeviceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var missingInstanceRows = available.Count(device =>
            device is null || string.IsNullOrWhiteSpace(device.DeviceId));
        var excluded = uniqueIdentities - trusted.Count + missingInstanceRows;

        var matches = new List<Match>();
        foreach (var device in trusted)
        {
            // The ZIP Inspector's fast preview must apply the SAME PCI
            // safety gate as full targeted INF/WUA correlation. A generic
            // PCI class or VEN-only token is shared with unrelated
            // NVIDIA/Killer devices and cannot prove any per-device match.
            // Keep it in the INF preview as declared metadata only.
            var hardware = new HashSet<string>(
                device.HardwareIds
                    .Where(DriverUpdateMatcher.IsCorrelatableHardwareId)
                    .Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
            var compatible = new HashSet<string>(
                device.CompatibleIds
                    .Where(DriverUpdateMatcher.IsCorrelatableHardwareId)
                    .Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var inf in evidence.InfFiles.Where(inf => inf.IsReadable))
            {
                var ids = evidence.HasTargetedDeviceMatches
                    ? evidence.TargetedMatches.TryGetValue(inf.Path, out var specific)
                        ? specific : []
                    : inf.DeclaredIds;
                foreach (var id in ids)
                {
                    // Also reject generic PCI declarations in older
                    // preview-only reports and synthetic/stale targeted
                    // snapshots: metadata != attributable device evidence.
                    if (!DriverUpdateMatcher.IsCorrelatableHardwareId(id.HardwareId))
                        continue;

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
            TrustedDevices = trusted.Count,
            ExcludedUnverifiedPnpIdentities = excluded,
            HasUnexaminedInf = evidence.RejectedInfCount > 0 ||
                !evidence.HasTargetedDeviceMatches && evidence.InfFiles.Any(
                    inf => inf.DeclaredIds.Count >= InfModelEvidence.MaximumPreviewIds)
        };
    }
}
