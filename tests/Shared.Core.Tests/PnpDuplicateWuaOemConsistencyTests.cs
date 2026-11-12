using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Contradictory same-instance PnP rows must not manufacture exact WUA,
/// weak WUA or newer OEM-INF evidence. Valid PnP ordinals stay unchanged.
/// </summary>
public sealed class PnpDuplicateWuaOemConsistencyTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaSpecific = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverInfo Device(string instance, string id,
        string name = "Generic Windows device") =>
        new(instance, name, "Microsoft", "Display", "Microsoft",
            "30.0.0.0", null, "PRIVATE_INF_oem42.inf", true, true, 0,
            [id], []);

    private static DriverUpdateInfo Wua(int n, string id) =>
        new($"00000000-0000-4000-8000-{n:D12}",
            "PRIVATE_DRIVER_TITLE", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(params DriverUpdateInfo[] updates) =>
        new(updates, [
            new DriverSearchPassStatus(
                "Windows Update online", "Completed", updates.Length, "verified")
        ]);

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\nClass=Display\n" +
        "CatalogFile.NTamd64=driver.cat\n" +
        "DriverVer=10/04/2026,32.0.15.7000\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64.10.0...22000\n" +
        "[Models.NTamd64.10.0...22000]\n%Model%=Install," + id + "\n";

    private static MemoryStream Zip(params string[] models)
    {
        var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < models.Length; i++)
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry($"pack/{i:D2}.inf").Open(), new UTF8Encoding(false));
                writer.Write(Inf(models[i]));
            }
            using var cat = new StreamWriter(
                zip.CreateEntry("pack/driver.cat").Open(), new UTF8Encoding(false));
            cat.Write("NOT_A_VERIFIED_SIGNATURE");
        }
        data.Position = 0;
        return data;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContradictoryNvidiaRealtekCannotCreateWuaOrOemMatches(
        bool reversed)
    {
        var gpu = Device("PRIVATE_NODE", NvidiaSpecific, "Basic Display");
        var codec = Device("private_node", Realtek, "Audio codec");
        var devices = reversed ? new[] { codec, gpu } : new[] { gpu, codec };
        var report = Report(Wua(1, NvidiaSpecific), Wua(2, Realtek), Wua(3, Nvidia));
        var audit = WuaPnpAssociationAudit.Create(devices, report, true);

        Assert.True(audit.SearchVerified);
        Assert.True(audit.InventoryVerified);
        Assert.Equal(3, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(0, audit.GenericModelOnly);
        Assert.Equal(3, audit.Unattributed);
        Assert.Contains("identità vendor contraddittorie", audit.Text);
        Assert.Contains("assenza di driver NON verificabile", audit.Text);
        Assert.False(audit.IsInstallable);
        Assert.False(audit.IsOemUpdateProof);
        Assert.DoesNotContain("PRIVATE", audit.Text);
        Assert.DoesNotContain(NvidiaSpecific, audit.Text);

        using var archive = Zip(NvidiaSpecific, Realtek, Nvidia);
        var oem = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        Assert.Equal(2, oem.DevicesExamined);
        Assert.Equal(3, oem.InfExamined);
        Assert.Empty(oem.Findings);
        Assert.Equal(0, oem.TotalMatchedDevices);
        Assert.Equal(0, oem.PotentialNewerDevices);
        Assert.Empty(oem.MatchedDeviceOrdinals!);
        Assert.Empty(oem.PnpMatchedDeviceOrdinals!);
        Assert.Contains(oem.Notes,
            note => note.Contains("contraddittori") && note.Contains("parziale"));
        Assert.False(oem.IsInstallable);
        Assert.False(oem.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE", oem.Summary);

        var copy = ScanCoverageText.Create(report, [], 2, 0, 0,
            devices, inventoryComplete: true);
        Assert.Contains("identità vendor contraddittorie", copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HealthyKillerKeepsOriginalOrdinalInTwoOemArchives(
        int originalOrdinal)
    {
        var gpu = Device("PRIVATE_AMBIGUOUS", Nvidia, "NVIDIA GPU");
        var codec = Device("private_ambiguous", Realtek, "Realtek Audio");
        var killer = Device("PRIVATE_GOOD_KILLER", Killer, "Killer Wi-Fi");
        var devices = originalOrdinal switch
        {
            0 => new[] { killer, gpu, codec },
            1 => [gpu, killer, codec],
            _ => [gpu, codec, killer]
        };
        var report = Report(Wua(1, Nvidia), Wua(2, Realtek), Wua(3, Killer));
        var audit = WuaPnpAssociationAudit.Create(devices, report, true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(2, audit.Unattributed);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);

        using var first = Zip(Nvidia, Killer);
        using var second = Zip(Realtek, Killer);
        var a = LocalOemDriverComparison.Compare(first, devices, "x64", 10, 0, 26100);
        var b = LocalOemDriverComparison.Compare(second, devices, "x64", 10, 0, 26100);

        Assert.Equal(3, a.DevicesExamined);
        Assert.Equal(3, b.DevicesExamined);
        Assert.Equal([originalOrdinal], a.MatchedDeviceOrdinals);
        Assert.Equal([originalOrdinal], b.MatchedDeviceOrdinals);
        Assert.Equal([originalOrdinal], a.PnpMatchedDeviceOrdinals);
        Assert.Equal([originalOrdinal], b.PnpMatchedDeviceOrdinals);
        Assert.Single(a.Findings);
        Assert.Single(b.Findings);
        Assert.True(a.Findings[0].HasExactHardwareId);
        Assert.True(a.Findings[0].IsPotentialNewer);
        Assert.Equal(originalOrdinal, a.Findings[0].DeviceOrdinal);

        var batch = LocalOemArchiveBatchSummary.Combine([a, b]);
        Assert.Equal(3, batch.DeviceCount);
        Assert.Equal(2, batch.TotalAssociations);
        Assert.Equal(1, batch.DistinctMatchedDevices);
        Assert.Equal(1, batch.RepeatedAssociations);
        Assert.Single(batch.Findings);
        Assert.Equal(originalOrdinal, batch.Findings[0].DeviceOrdinal);
        Assert.True(batch.IsPartial);
        Assert.False(batch.IsInstallable);
        Assert.DoesNotContain("PRIVATE", batch.Summary);
    }

    [Fact]
    public void ConsistentDuplicateRetainsOneGenuineNvidiaAssociation()
    {
        var gpu = Device("PRIVATE_GPU", Nvidia, "NVIDIA GPU");
        var same = gpu with
        {
            DeviceId = "private_gpu",
            DriverProvider = "NVIDIA",
            Name = "GeForce RTX"
        };
        var audit = WuaPnpAssociationAudit.Create(
            [gpu, same], Report(Wua(1, Nvidia)), true);
        Assert.Equal(0, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);

        using var zip = Zip(Nvidia);
        var oem = LocalOemDriverComparison.Compare(
            zip, [gpu, same], "x64", 10, 0, 26100);
        Assert.Equal(2, oem.DevicesExamined);
        Assert.Equal(1, oem.TotalMatchedDevices);
        Assert.Equal(1, oem.PotentialNewerDevices);
        Assert.Equal([0], oem.MatchedDeviceOrdinals);
        Assert.Single(oem.Findings);
        Assert.DoesNotContain(oem.Notes, n => n.Contains("vendor contraddittori"));
    }

    [Fact]
    public void ConflictingWithinOneRowOrMissingInstanceDoesNotMatch()
    {
        var mixed = Device("PRIVATE_CONFLICT", Nvidia) with
        {
            HardwareIds = [Nvidia, Realtek]
        };
        var noInstance = Device("", Nvidia, "Unnamed");
        var report = Report(Wua(1, Nvidia), Wua(2, Realtek));
        var audit = WuaPnpAssociationAudit.Create([mixed, noInstance], report, true);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(2, audit.Unattributed);

        using var zip = Zip(Nvidia, Realtek);
        var oem = LocalOemDriverComparison.Compare(
            zip, [mixed, noInstance], "x64", 10, 0, 26100);
        Assert.Empty(oem.Findings);
        Assert.Empty(oem.MatchedDeviceOrdinals!);
        Assert.Contains(oem.Notes, n => n.Contains("vendor contraddittori"));
    }
}
