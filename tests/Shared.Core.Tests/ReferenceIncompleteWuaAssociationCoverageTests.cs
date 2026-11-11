using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// The eight Driver Easy comparison rows cannot infer "no WUA offers"
/// when a confirmed Type=Driver GUID's device-ID tail was not enumerated.
/// This is a diagnostic caveat, never a fabricated exact PnP match.
/// </summary>
public sealed class ReferenceIncompleteWuaAssociationCoverageTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string IntelBluetooth = @"USB\VID_8087&PID_0033";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Unrelated = @"PCI\VEN_1234&DEV_5678";
    private const string GuidOne = "00000000-0000-4000-8000-000000000370";
    private const string GuidTwo = "00000000-0000-4000-8000-000000000371";

    private static DriverInfo Device(string instance, string id,
        string deviceClass, string name) =>
        new(instance, name, "Microsoft", deviceClass, "Microsoft",
            "1.0", null, "private-driver.inf", true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(string id, string hardware,
        bool incomplete, string source = "Windows Update online") =>
        new(id, "PRIVATE_UPDATE_TITLE", null, "System",
            "Microsoft", null, null, false, "Microsoft", null,
            hardware, null, SearchSource: source,
            AdditionalHardwareIdsIncomplete: incomplete);

    private static DriverSearchPassStatus Pass(
        string outcome = "Completed") =>
        new("Windows Update online", outcome, 1,
            "PRIVATE_WSUS_HOSTNAME");

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> updates, string outcome = "Completed") =>
        new(updates, [Pass(outcome)]);

    [Fact]
    public void IncompleteWuaIdsShowUnknownAbsenceForNvidiaKillerAndBluetooth()
    {
        var devices = new[]
        {
            Device("PRIVATE_GPU_INSTANCE", Nvidia, "Display",
                "Microsoft Basic Display Adapter"),
            Device("PRIVATE_KILLER_INSTANCE", Killer, "Net",
                "Killer Wi-Fi 6E"),
            Device("PRIVATE_BT_INSTANCE", IntelBluetooth, "Bluetooth",
                "Generic Bluetooth"),
            Device("PRIVATE_AUDIO_INSTANCE", NvidiaAudio, "MEDIA",
                "High Definition Audio Device")
        };
        var report = Report([Offer(GuidOne, Unrelated, incomplete: true)]);
        var coverage = ReferenceDriverCoverage.Create(devices, report);
        foreach (var index in new[] { 1, 4, 5, 6 })
        {
            Assert.Equal(1, coverage[index].Devices);
            Assert.Equal(0, coverage[index].WindowsDriverCandidates);
            Assert.True(coverage[index].WuaAssociationsIncomplete);
            Assert.Contains("assenza non verificabile",
                coverage[index].DisplayLine(true, true, false));
        }
        Assert.False(coverage[3].WuaAssociationsIncomplete);

        var presentation = ReferenceCoveragePresentation.Create(
            devices, report, inventoryComplete: true);
        foreach (var index in new[] { 1, 4, 5, 6 })
        {
            Assert.Contains("0 match osservati",
                presentation[index].WindowsDriverStatus);
            Assert.Contains("assenza non verificabile",
                presentation[index].WindowsDriverStatus);
            Assert.Contains("fonti ufficiali", presentation[index].NextStep);
            Assert.False(presentation[index].IsInstallable);
        }
        var copied = ScanCoverageText.Create(
            report, [], devices.Length, 0, 0,
            devices, inventoryComplete: true);
        Assert.Contains("Confronto categorie Intel/Killer/NVIDIA", copied);
        Assert.Contains("ID WUA incompleti: 0 match osservati",
            copied);
        Assert.DoesNotContain("PRIVATE", copied);
        Assert.DoesNotContain(Nvidia, copied);
        Assert.DoesNotContain(IntelBluetooth, copied);
        Assert.DoesNotContain(Killer, copied);
        Assert.DoesNotContain(GuidOne, copied);
        Assert.DoesNotContain(Unrelated, copied);
    }

    [Fact]
    public void ExactMatchedGuidStillCountsOnceAndNotesPossibleOtherAssociations()
    {
        var gpu = Device("PRIVATE_NVIDIA", Nvidia, "Display",
            "Microsoft Basic Display Adapter");
        var audio = Device("PRIVATE_AUDIO", NvidiaAudio, "MEDIA",
            "High Definition Audio Device");
        // Same WUA GUID from two trusted sources with an incomplete
        // HardwareID tail: exact matching survives, never a second GUID.
        var report = Report(
        [
            Offer(GuidOne, Nvidia, incomplete: false,
                source: "Windows configurato"),
            Offer("{" + GuidOne + "}", Unrelated, incomplete: true,
                source: "Microsoft Update online")
        ]);
        var coverage = ReferenceDriverCoverage.Create([gpu, audio], report);
        Assert.Equal(1, coverage[5].WindowsDriverCandidates);
        Assert.Equal(0, coverage[6].WindowsDriverCandidates);
        Assert.True(coverage[5].WuaAssociationsIncomplete);
        Assert.True(coverage[6].WuaAssociationsIncomplete);
        Assert.Contains("ulteriori associazioni possibili",
            coverage[5].DisplayLine(true, true, false));
        Assert.Contains("assenza non verificabile",
            coverage[6].DisplayLine(true, true, false));
        var displayed = ReferenceCoveragePresentation.Create(
            [gpu, audio], report, true);
        Assert.Contains("1 candidati driver Windows",
            displayed[5].WindowsDriverStatus);
        Assert.Contains("ulteriori associazioni possibili",
            displayed[5].WindowsDriverStatus);
        Assert.False(displayed[5].IsInstallable);
    }

    [Fact]
    public void CompleteWuaIdsStillAllowFactualZeroObservedWithoutFalseCaveat()
    {
        var device = Device("PRIVATE_GPU", Nvidia, "Display",
            "Microsoft Basic Display Adapter");
        var report = Report([Offer(GuidOne, Unrelated, incomplete: false)]);
        var row = ReferenceDriverCoverage.Create([device], report)[5];
        Assert.Equal(0, row.WindowsDriverCandidates);
        Assert.False(row.WuaAssociationsIncomplete);
        Assert.DoesNotContain("ID WUA incompleti",
            row.DisplayLine(true, true, false));
        var presented = ReferenceCoveragePresentation.Create(
            [device], report, true)[5];
        Assert.Contains("0 candidati", presented.WindowsDriverStatus);
        Assert.DoesNotContain("assenza non verificabile",
            presented.WindowsDriverStatus);
    }

    [Fact]
    public void UnverifiedOemOrMalformedGuidCannotInventIncompleteWuaStatus()
    {
        var device = Device("PRIVATE_GPU", Nvidia, "Display",
            "Microsoft Basic Display Adapter");
        var report = Report(
        [
            Offer(GuidOne, Unrelated, incomplete: true,
                source: "Unverified OEM repository"),
            Offer("PRIVATE_NOT_GUID", Unrelated, incomplete: true)
        ]);
        var row = ReferenceDriverCoverage.Create([device], report)[5];
        Assert.Equal(0, row.WindowsDriverCandidates);
        Assert.False(row.WuaAssociationsIncomplete);
        Assert.DoesNotContain("ID WUA incompleti",
            row.DisplayLine(true, true, false));
        Assert.False(ReferenceCoveragePresentation.Create(
            [device], report, true)[5].IsInstallable);
    }

    [Fact]
    public void AllFailedSearchCannotTurnStaleIncompleteGuidIntoVerifiedCoverage()
    {
        var device = Device("PRIVATE_KILLER", Killer, "Net",
            "Killer Wi-Fi 6E");
        var failed = Report(
            [Offer(GuidTwo, Unrelated, incomplete: true)],
            outcome: "Failed");
        var row = ReferenceDriverCoverage.Create([device], failed)[4];
        Assert.False(row.WuaAssociationsIncomplete);
        var presented = ReferenceCoveragePresentation.Create(
            [device], failed, true)[4];
        Assert.Contains("non verificata", presented.WindowsDriverStatus);
        Assert.DoesNotContain("0 candidati",
            presented.WindowsDriverStatus);
        Assert.DoesNotContain("ID WUA incompleti",
            presented.WindowsDriverStatus);
        // The same partial evidence cannot prove PnP identity.
        var noInventory = ReferenceCoveragePresentation.Create(
            [device], Report([Offer(GuidTwo, Unrelated, true)]),
            inventoryComplete: false)[4];
        Assert.Contains("non verificato", noInventory.PnpStatus);
        Assert.DoesNotContain("ID WUA incompleti",
            noInventory.WindowsDriverStatus);
    }

    [Fact]
    public void PartialWuaPassAndIncompleteIdTailAreTwoDistinctLimitations()
    {
        var device = Device("PRIVATE_GPU", Nvidia, "Display",
            "Microsoft Basic Display Adapter");
        var report = new DriverSearchReport(
            [Offer(GuidOne, Unrelated, incomplete: true)],
            [Pass(), new DriverSearchPassStatus(
                "Microsoft Update online", "NetworkError", 0,
                "PRIVATE_PROXY_ENDPOINT")]);
        var row = ReferenceCoveragePresentation.Create(
            [device], report, true)[5];
        Assert.Contains("ricerca parziale",
            row.WindowsDriverStatus);
        Assert.Contains("ID WUA incompleti",
            row.WindowsDriverStatus);
        Assert.False(row.IsInstallable);
        var text = ScanCoverageText.Create(
            report, [], 1, 0, 0, [device], inventoryComplete: true);
        Assert.Contains("ricerca parziale", text);
        Assert.Contains("ID WUA incompleti", text);
        Assert.DoesNotContain("PRIVATE_", text);
    }
}
