using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LocalOemArchiveBatchSummaryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";

    private static LocalOemDriverComparison.Finding Finding(
        string name, string infName, bool newer = true) => new(
            name, infName, "31.0.0.0",
            newer ? "32.0.0.0" : "30.0.0.0",
            2, InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            newer
                ? LocalOemDriverComparison.VersionRelation.NewerHint
                : LocalOemDriverComparison.VersionRelation.OlderHint);

    private static LocalOemDriverComparison.Report Report(
        int devices, IReadOnlyList<LocalOemDriverComparison.Finding> findings,
        int omitted = 0, int rejected = 0) => new(
            2, devices, findings, 0, [], rejected, 2, 0, omitted);

    [Fact]
    public void IntelDominatedFirstPackageCannotHideSingleNvidiaInSecondPackage()
    {
        var intel = Enumerable.Range(0, 80)
            .Select(i => Finding($"Intel network {i}", $"private-order-{i}.inf"))
            .ToArray();
        var nvidia = Finding("NVIDIA RTX 4060", "ORDER-998877-GPU.inf");
        var result = LocalOemArchiveBatchSummary.Combine(
        [
            Report(101, intel, omitted: 21),
            Report(101, [nvidia])
        ]);

        Assert.Equal(2, result.ArchivesExamined);
        Assert.Equal(101, result.DeviceCount);
        Assert.Equal(102, result.TotalAssociations);
        Assert.Equal(80, result.Findings.Count);
        Assert.Equal(22, result.RowsOmitted);
        Assert.Equal(80, result.PotentialNewerShown);
        Assert.Contains(result.Findings, f =>
            f.DeviceName == "NVIDIA RTX 4060" &&
            f.InfName.StartsWith("Archivio 02", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f =>
            f.InfName.StartsWith("Archivio 01", StringComparison.Ordinal));

        Assert.Contains("NON dispositivi unici", result.Summary);
        Assert.Contains("22 non mostrate", result.Summary);
        Assert.DoesNotContain("ORDER-998877", result.Summary);
        Assert.DoesNotContain("private-order", result.Summary);
        Assert.All(result.Findings, f =>
        {
            Assert.DoesNotContain("private-order", f.InfName);
            Assert.DoesNotContain("ORDER-998877", f.InfName);
        });
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Fact]
    public void SameGpuInTwoOemArchivesCountsTwoAssociationsNotUniqueDevices()
    {
        var gpu = Finding("NVIDIA RTX 4060", "order-PRIVATE-CAT.inf");
        var result = LocalOemArchiveBatchSummary.Combine(
        [
            Report(3, [gpu]),
            Report(3, [gpu])
        ]);
        Assert.Equal(2, result.TotalAssociations);
        Assert.Equal(2, result.Findings.Count);
        Assert.All(result.Findings, f =>
            Assert.Equal("NVIDIA RTX 4060", f.DeviceName));
        Assert.NotEqual(result.Findings[0].InfName, result.Findings[1].InfName);
        Assert.Contains("stessa GPU", string.Join(" ", result.Notes));
        Assert.False(result.IsInstallable);
        Assert.DoesNotContain("PRIVATE", result.Summary);
    }

    [Fact]
    public void TwentyFourPackagesAreAllowedAndAllReceiveFairDisplaySlots()
    {
        var packages = Enumerable.Range(0,
            LocalOemArchiveBatchSummary.MaximumArchives)
            .Select(i => Report(100,
                Enumerable.Range(0, 50)
                    .Select(j => Finding($"GPU {i}-{j}", $"hidden{i}-{j}.inf",
                        newer: j % 2 == 0)).ToArray()))
            .ToArray();

        var result = LocalOemArchiveBatchSummary.Combine(packages);
        Assert.Equal(24, result.ArchivesExamined);
        Assert.Equal(1200, result.TotalAssociations);
        Assert.Equal(80, result.Findings.Count);
        Assert.Equal(1120, result.RowsOmitted);
        // 3 fair rounds for all 24 packages (48 even-depth newer
        // versions); remaining 8 rows are the older depth-3 entries.
        Assert.Equal(48, result.PotentialNewerShown);
        Assert.All(Enumerable.Range(1, 24), number =>
            Assert.Contains(result.Findings, finding =>
                finding.InfName.StartsWith(
                    $"Archivio {number:D2}", StringComparison.Ordinal)));
        Assert.Contains(result.Notes, note => note.Contains("non esaustivo"));
        Assert.False(result.IsVerifiedCompatible);
    }


    [Fact]
    public void TwentyFourSourcesDeduplicateTwentyTwoRealtekCopiesButRetainLateGpuAndKiller()
    {
        // The ordinal is the only device identity. Vendor/driver titles
        // are never used to merge or inflate the number of OEM updates.
        var reports = Enumerable.Range(0, LocalOemArchiveBatchSummary.MaximumArchives)
            .Select(i =>
            {
                var ordinal = i < 22 ? 0 : i == 22 ? 1 : 2;
                var label = ordinal switch
                {
                    0 => "PRIVATE_REALTEK",
                    1 => "NVIDIA RTX 4060",
                    _ => "Killer Wi-Fi"
                };
                return Report(3, [
                    Finding(label, "PRIVATE_ORDER_99331.inf") with
                    {
                        DeviceOrdinal = ordinal
                    }
                ]) with { MatchedDeviceOrdinals = [ordinal] };
            }).ToArray();

        var combined = LocalOemArchiveBatchSummary.Combine(reports);
        Assert.Equal(24, combined.ArchivesExamined);
        Assert.Equal(24, combined.TotalAssociations);
        Assert.Equal(3, combined.DistinctMatchedDevices);
        Assert.Equal(21, combined.RepeatedAssociations);
        Assert.Equal(3, combined.Findings.Count);
        Assert.Contains(combined.Findings, row =>
            row.DeviceName == "NVIDIA RTX 4060" &&
            row.InfName.Contains("Archivio 23"));
        Assert.Contains(combined.Findings, row =>
            row.DeviceName == "Killer Wi-Fi" &&
            row.InfName.Contains("Archivio 24"));
        Assert.DoesNotContain("PRIVATE_ORDER", combined.Summary);
        Assert.All(combined.Findings, row =>
            Assert.DoesNotContain("PRIVATE_ORDER", row.InfName));
        Assert.False(combined.IsInstallable);
        Assert.False(combined.IsVerifiedCompatible);
    }

    [Fact]
    public void EmptyOversizedOrMismatchedInventoriesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine([]));
        var tooMany = Enumerable.Range(0,
            LocalOemArchiveBatchSummary.MaximumArchives + 1)
            .Select(_ => Report(1, []))
            .ToArray();
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine(tooMany));
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine(
            [
                Report(2, [Finding("NVIDIA", "a.inf")]),
                Report(3, [Finding("NVIDIA", "b.inf")])
            ]));
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine(
                [new LocalOemDriverComparison.Report(0, 1, [], 0, [], RejectedInfCount: 1)]));
        Assert.Throws<ArgumentNullException>(() =>
            LocalOemArchiveBatchSummary.Combine(null!));
    }

    [Fact]
    public void RejectedInfOrTruncatedPerPackageEvidenceMarksWholeBatchPartial()
    {
        var result = LocalOemArchiveBatchSummary.Combine(
        [
            Report(100, [Finding("NVIDIA", "gpu.inf")],
                omitted: 5, rejected: 1),
            Report(100, [])
        ]);
        Assert.Equal(1, result.RejectedInfCount);
        Assert.Equal(6, result.TotalAssociations);
        Assert.Equal(5, result.RowsOmitted);
        Assert.Contains(result.Notes, note => note.Contains("parziale"));
        Assert.Single(result.Findings);
    }

    private static DriverInfo Pnp(string instance, string name, string id,
        string driverVersion) =>
        new(instance, name, "Microsoft", "Display", "Microsoft",
            driverVersion, null, "oem3.inf", true, true, 0,
            [id], []);

    private static MemoryStream Zip(string path, string inf)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(),
                new UTF8Encoding(false));
            writer.Write(inf);
        }
        memory.Position = 0;
        return memory;
    }

    private static string Inf(string hardwareId, string version) =>
        $"""
        [Version]
        Signature="$Windows NT$"
        DriverVer=10/04/2026,{version}
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        Model=Install,{hardwareId}
        """;

    [Fact]
    public void TwoRealZipComparisonsKeepIndependentNvidiaAndKillerEvidence()
    {
        var devices = new[]
        {
            Pnp("PRIVATE_GPU_INSTANCE", "NVIDIA RTX", Nvidia, "31.0.0.0"),
            Pnp("PRIVATE_KILLER_INSTANCE", "Killer Wi-Fi", Killer, "23.0.0.0")
        };
        using var gpuZip = Zip("private/order-991199-nvidia.inf",
            Inf(Nvidia, "32.0.15.7000"));
        using var killerZip = Zip("private/order-991199-killer.inf",
            Inf(Killer, "24.0.1.0"));

        var first = LocalOemDriverComparison.Compare(
            gpuZip, devices, "x64", 10, 0, 26100);
        var second = LocalOemDriverComparison.Compare(
            killerZip, devices, "x64", 10, 0, 26100);
        var batch = LocalOemArchiveBatchSummary.Combine([first, second]);

        Assert.Equal(2, batch.ArchivesExamined);
        Assert.Equal(2, batch.TotalAssociations);
        Assert.Equal(2, batch.Findings.Count);
        Assert.Equal(2, batch.PotentialNewerShown);
        Assert.Contains(batch.Findings, f =>
            f.DeviceName == "NVIDIA RTX" && f.HasExactHardwareId);
        Assert.Contains(batch.Findings, f =>
            f.DeviceName == "Killer Wi-Fi" && f.HasExactHardwareId);
        Assert.DoesNotContain("991199", batch.Summary);
        Assert.All(batch.Findings, f => Assert.DoesNotContain("991199", f.InfName));
        Assert.False(batch.IsInstallable);
        Assert.False(batch.IsVerifiedCompatible);
    }

    [Fact]
    public void OldNestedCoverageFlagWithoutTypedCountsStaysPartial()
    {
        // Historical reports do not carry typed omissions. Never infer a
        // depth overrun from a legacy flag that could represent an EXE.
        var previous = Report(2, []) with
        {
            HasUninspectedNestedArchives = true
        };
        var result = LocalOemArchiveBatchSummary.Combine([previous]);
        Assert.True(result.IsPartial);
        Assert.Equal(0, result.UninspectedNestedExecutables);
        Assert.Equal(0, result.UninspectedProprietaryPackages);
        Assert.Equal(0, result.UninspectedDeeperArchives);
        Assert.Contains(result.Notes, note =>
            note.Contains("causa non precisata") &&
            note.Contains("copertura parziale"));
        Assert.DoesNotContain(result.Notes, note =>
            note.Contains("ZIP/CAB oltre la profondità"));
        Assert.Contains("COPERTURA PARZIALE", result.Summary);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void NegativeTypedOmissionCountersAreRejectedFailClosed()
    {
        var corrupted = Report(2, []) with
        {
            UninspectedNestedExecutables = -1
        };
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine([corrupted]));

        corrupted = Report(2, []) with
        {
            UninspectedDeeperArchives = -1
        };
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine([corrupted]));

        corrupted = Report(2, []) with
        {
            UninspectedProprietaryPackages = -1
        };
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine([corrupted]));
    }

}
