using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class HomeVendorCoverageSummaryTests
{
    private static DriverInfo Device(string instance, string title,
        string deviceClass, string id) =>
        new(instance, title, "Microsoft", deviceClass, "Microsoft",
            "31.0.0.1", null, "oem9.inf", true, true, 0, [id], []);

    private static DriverUpdateInfo Update(string guid, string id) =>
        new(guid, "OEM driver from Windows", null, "Display",
            "Microsoft", null, 2048, false, "OEM", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchPassStatus Pass(string outcome) =>
        new("Windows Update online", outcome, 0, "private diagnostics");

    private static readonly DriverInfo Nvidia =
        Device("PRIVATE_GPU", "Microsoft Basic Display Adapter", "Display",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA");
    private static readonly DriverInfo Killer =
        Device("PRIVATE_NETWORK", "Killer Wi-Fi 6E", "Net",
            @"PCI\VEN_8086&DEV_51F1");
    private static readonly DriverInfo Realtek =
        Device("PRIVATE_AUDIO", "High Definition Audio Device", "MEDIA",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295");

    [Fact]
    public void RecognizesGenericNvidiaKillerRealtekAndDeduplicatesWindowsGuid()
    {
        var id = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport(
            [Update(id, Nvidia.HardwareIds[0]),
             Update(id.ToUpperInvariant(), Nvidia.HardwareIds[0]),
             Update(Guid.NewGuid().ToString("D"), Killer.HardwareIds[0])],
            [Pass("Completed")]);

        var rows = HomeVendorCoverageSummary.Create(
            [Nvidia, Nvidia with { DeviceId = "private_gpu" }, Killer, Realtek],
            report, true);

        Assert.Equal(["NVIDIA", "Intel / Killer", "Realtek"],
            rows.Select(row => row.Group).ToArray());
        Assert.All(rows, row => Assert.Contains(
            "1 dispositivo PnP", row.DeviceStatus));
        Assert.Contains("1 candidato WUA", rows[0].WindowsStatus);
        Assert.Contains("1 candidato WUA", rows[1].WindowsStatus);
        Assert.Contains("0 candidati WUA", rows[2].WindowsStatus);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
        var text = string.Join(" ", rows.SelectMany(row =>
            new[] { row.Group, row.DeviceStatus, row.WindowsStatus }));
        Assert.DoesNotContain("PRIVATE", text);
        Assert.DoesNotContain(@"PCI\", text);
        Assert.DoesNotContain(@"HDAUDIO\", text);
        Assert.DoesNotContain("OEM driver", text);
    }

    [Fact]
    public void LaterSourceNvidiaAndRealtekIdsStayVisibleInHomeCards()
    {
        const string guid = "00000000-0000-4000-8000-000000000271";
        var first = Update(guid, @"PCI\VEN_9999&DEV_0001");
        var second = Update(guid.ToUpperInvariant(), Nvidia.HardwareIds[0]) with
        {
            AdditionalHardwareIds = [Realtek.HardwareIds[0]],
            IsAdvancedCandidate = true
        };
        var report = new DriverSearchReport([first, second],
            [Pass("Completed"), Pass("Completed")]);
        var rows = HomeVendorCoverageSummary.Create(
            [Nvidia, Killer, Realtek], report, true);
        Assert.Contains("1 candidato WUA per ID", rows[0].WindowsStatus);
        Assert.Contains("0 candidati WUA per ID", rows[1].WindowsStatus);
        Assert.Contains("1 candidato WUA per ID", rows[2].WindowsStatus);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
        Assert.DoesNotContain("PRIVATE", string.Join(" ",
            rows.Select(row => row.WindowsStatus)));
    }

    [Fact]
    public void NoInventoryOrFailedSearchNeverMeansZeroDriversAvailable()
    {
        var failed = new DriverSearchReport([], [Pass("Failed")]);
        var noInventory = HomeVendorCoverageSummary.Create(
            [Nvidia], failed, false);
        Assert.All(noInventory, row =>
        {
            Assert.Contains("PnP non verificato", row.DeviceStatus);
            Assert.Contains("non disponibile", row.WindowsStatus);
            Assert.DoesNotContain("0 candidati", row.WindowsStatus);
        });

        var unavailable = HomeVendorCoverageSummary.Create(
            [Nvidia], failed, true);
        Assert.Contains("1 dispositivo", unavailable[0].DeviceStatus);
        Assert.All(unavailable, row =>
            Assert.Contains("non verificata", row.WindowsStatus));
        Assert.DoesNotContain(unavailable,
            row => row.WindowsStatus.Contains("0 candidati"));
    }

    [Fact]
    public void PartialWuaScanClearlyStatesLimitedCoverage()
    {
        var report = new DriverSearchReport([], [
            Pass("Completed"), Pass("Failed")
        ]);
        var rows = HomeVendorCoverageSummary.Create(
            [Nvidia], report, true);

        Assert.Contains("0 candidati WUA", rows[0].WindowsStatus);
        Assert.Contains("parziali", rows[0].WindowsStatus);
        Assert.Contains("0 dispositivi", rows[1].DeviceStatus);
        Assert.Contains("0 dispositivi", rows[2].DeviceStatus);
        Assert.DoesNotContain("aggiornato", rows[0].WindowsStatus);
    }

    [Fact]
    public void TypeSoftwareAndUnrelatedIntelBridgesDoNotInflateDriverCounts()
    {
        var unrelated = Device("PRIVATE_BRIDGE", "Intel Host Bridge",
            "System", @"PCI\VEN_8086&DEV_1234");
        var report = new DriverSearchReport(
            [], [Pass("Completed")],
            [new WuaDriverRelatedSoftware(
                Guid.NewGuid().ToString("D"),
                "Killer Networking Software", "Windows Update online")],
            [Pass("Completed")]);
        var rows = HomeVendorCoverageSummary.Create(
            [unrelated, Killer], report, true);

        Assert.Equal("1 dispositivo PnP classificato", rows[1].DeviceStatus);
        Assert.Contains("0 candidati WUA", rows[1].WindowsStatus);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
    }

    [Fact]
    public void UnknownPciIntelGpuAndKillerShowOnHomeAsInEightRowReport()
    {
        const string intelId = @"PCI\VEN_8086&DEV_46A6";
        const string killerId = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
        var genericGpu = Device("PRIVATE_INTEL_GPU", "PCI Device",
            "Unknown", intelId) with
        {
            CompatibleIds = [@"PCI\CC_030000"]
        };
        var genericKiller = Device("PRIVATE_KILLER", "PCI Device",
            "System", killerId) with
        {
            CompatibleIds = [@"PCI\CC_028000"]
        };
        var intelGuid = Guid.NewGuid().ToString("D");
        var killerGuid = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport(
            [Update(intelGuid, intelId), Update(killerGuid, killerId),
             Update(killerGuid.ToUpperInvariant(), killerId)],
            [Pass("Completed")]);

        var home = HomeVendorCoverageSummary.Create(
            [genericGpu, genericKiller, genericKiller with
            {
                DeviceId = "private_killer"
            }], report, true);

        Assert.Equal("2 dispositivi PnP classificati", home[1].DeviceStatus);
        Assert.Contains("2 candidati WUA per ID", home[1].WindowsStatus);
        Assert.True(home[1].CanInspectLocalDrivers);
        Assert.False(home[1].IsInstallable);
        Assert.Equal("0 dispositivi PnP classificati", home[0].DeviceStatus);
        Assert.Equal("0 dispositivi PnP classificati", home[2].DeviceStatus);

        var eightRows = ReferenceDriverCoverage.Create(
            [genericGpu, genericKiller], report);
        Assert.Equal(1, eightRows[0].Devices);
        Assert.Equal(1, eightRows[0].WindowsDriverCandidates);
        Assert.Equal(1, eightRows[4].Devices);
        Assert.Equal(1, eightRows[4].WindowsDriverCandidates);

        // The Home click selects the actual Killer device (not the Intel
        // GPU), and starts only a user-initiated read-only PnPUtil lookup.
        var selected = HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [genericGpu, genericKiller], "Intel / Killer", true);
        Assert.Equal(genericKiller.DeviceId, selected?.DeviceId);

        var copy = string.Join(" ", home.Select(x =>
            x.DeviceStatus + " " + x.WindowsStatus));
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain("SUBSYS_", copy);
        Assert.DoesNotContain(@"PCI\", copy);
        Assert.DoesNotContain(intelGuid, copy);
        Assert.DoesNotContain(killerGuid, copy);
    }

    [Fact]
    public void GenericIntelDeviceWithoutVerifiedClassIsNotPromotedToHome()
    {
        const string chipsetId = @"PCI\VEN_8086&DEV_1234";
        const string fakeKiller = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A560";
        var chipset = Device("PRIVATE_CHIPSET", "PCI Device", "Unknown",
            chipsetId) with
        {
            CompatibleIds = [@"PCI\CC_060000"]
        };
        var counterfeit = Device("PRIVATE_FAKE", "PCI Device", "Unknown",
            fakeKiller) with { CompatibleIds = [@"PCI\CC_0600"] };
        var contradictory = Device("PRIVATE_CAMERA", "PCI Device", "Camera",
            @"PCI\VEN_8086&DEV_46A6") with
        {
            CompatibleIds = [@"PCI\CC_030000"]
        };

        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(counterfeit));
        var rows = HomeVendorCoverageSummary.Create(
            [chipset, counterfeit, contradictory], null, true);
        Assert.Equal("0 dispositivi PnP classificati", rows[1].DeviceStatus);
        Assert.Contains("non verificata", rows[1].WindowsStatus);
        Assert.False(rows[1].CanInspectLocalDrivers);
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [chipset, counterfeit, contradictory], "Intel / Killer", true));
    }

    [Fact]
    public void GenericPciNetworkIsIntelWithoutFalselyClaimingKillerBrand()
    {
        var pci = Device("PRIVATE_INTEL_NET", "PCI Device",
            "Unknown", @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086") with
        {
            CompatibleIds = [@"PCI\CC_0280"]
        };
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(pci));
        Assert.Equal("Intel",
            VendorSourceDiscovery.GetRecognizedSupportVendor(pci));
        var rows = HomeVendorCoverageSummary.Create(
            [pci], new DriverSearchReport([], [Pass("Completed")]), true);
        Assert.Equal("1 dispositivo PnP classificato", rows[1].DeviceStatus);
        Assert.Contains("0 candidati WUA per ID", rows[1].WindowsStatus);
        Assert.False(rows[1].IsInstallable);
    }

    [Fact]
    public void GenericPciIntelGpuDuringPartialOrFailedWuaHasNoFalseOffer()
    {
        var intel = Device("PRIVATE_INTEL", "PCI Device", "Unknown",
            @"PCI\VEN_8086&DEV_46A6") with
        {
            CompatibleIds = [@"PCI\CC_030000"]
        };
        var partial = new DriverSearchReport([], [
            Pass("Completed"), Pass("Failed")
        ]);
        var rows = HomeVendorCoverageSummary.Create([intel], partial, true);
        Assert.Contains("1 dispositivo PnP", rows[1].DeviceStatus);
        Assert.Contains("0 candidati WUA", rows[1].WindowsStatus);
        Assert.Contains("parziali", rows[1].WindowsStatus);
        Assert.True(rows[1].CanInspectLocalDrivers);

        var failed = HomeVendorCoverageSummary.Create(
            [intel], new DriverSearchReport([], [Pass("Failed")]), true);
        Assert.Contains("1 dispositivo PnP", failed[1].DeviceStatus);
        Assert.Contains("non verificata", failed[1].WindowsStatus);
        Assert.DoesNotContain("0 candidati", failed[1].WindowsStatus);
        Assert.False(failed[1].IsInstallable);
    }


    [Fact]
    public void ContradictoryKillerNameDoesNotOverrideRealtekHomeGroup()
    {
        var realtek = Device("PRIVATE_RT_NET", "Killer Wireless Adapter",
            "Net", @"PCI\VEN_10EC&DEV_8125");
        var intel = Device("PRIVATE_INTEL", "Killer Wi-Fi 6E",
            "Net", @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56");
        var rows = HomeVendorCoverageSummary.Create(
            [realtek, intel],
            new DriverSearchReport([], [Pass("Completed")]), true);
        Assert.Equal("1 dispositivo PnP classificato", rows[1].DeviceStatus);
        Assert.Equal("1 dispositivo PnP classificato", rows[2].DeviceStatus);
        Assert.Equal(intel.DeviceId,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                [realtek, intel], "Intel / Killer", true)?.DeviceId);
        Assert.Equal(realtek.DeviceId,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                [realtek, intel], "Realtek", true)?.DeviceId);
        Assert.All(rows, x => Assert.False(x.IsInstallable));
    }



    [Fact]
    public void HomeShowsActualPciVendorInsteadOfMisleadingNvidiaProvider()
    {
        const string intelId = @"PCI\VEN_8086&DEV_46A6";
        const string nvidiaId = @"PCI\VEN_10DE&DEV_28A0";
        const string realtekId = @"PCI\VEN_10EC&DEV_8125";

        var intel = Device("PRIVATE_INTEL", "NVIDIA Graphics Adapter",
            "Display", intelId) with
        {
            Manufacturer = "NVIDIA",
            DriverProvider = "NVIDIA"
        };
        var nvidia = Device("PRIVATE_NVIDIA", "Intel Graphics",
            "Display", nvidiaId) with
        {
            Manufacturer = "Intel",
            DriverProvider = "Intel"
        };
        var realtek = Device("PRIVATE_REALTEK", "NVIDIA Ethernet",
            "Net", realtekId) with
        {
            Manufacturer = "NVIDIA",
            DriverProvider = "NVIDIA"
        };
        var guidIntel = Guid.NewGuid().ToString("D");
        var guidNvidia = Guid.NewGuid().ToString("D");
        var guidRealtek = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport(
        [
            Update(guidIntel, intelId),
            Update(guidNvidia, nvidiaId),
            Update(guidRealtek, realtekId)
        ], [Pass("Completed")],
        [
            new WuaDriverRelatedSoftware(Guid.NewGuid().ToString("D"),
                "NVIDIA network utility", "Windows Update online")
        ], [Pass("Completed")]);
        var devices = new[]
        {
            intel, nvidia, realtek,
            realtek with { DeviceId = "private_realtek" }
        };
        var home = HomeVendorCoverageSummary.Create(devices, report, true);
        Assert.Equal(3, home.Count);
        Assert.All(home, row =>
        {
            Assert.Equal("1 dispositivo PnP classificato", row.DeviceStatus);
            Assert.Contains("1 candidato WUA per ID", row.WindowsStatus);
            Assert.False(row.IsInstallable);
            Assert.True(row.CanInspectLocalDrivers);
        });

        Assert.Equal(nvidia.DeviceId,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, "NVIDIA", true)?.DeviceId);
        Assert.Equal(intel.DeviceId,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, "Intel / Killer", true)?.DeviceId);
        Assert.Equal(realtek.DeviceId,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, "Realtek", true)?.DeviceId);

        var matrix = ReferenceDriverCoverage.Create(devices, report);
        Assert.Equal(1, matrix[0].Devices);
        Assert.Equal(1, matrix[5].Devices);
        Assert.Equal(1, matrix[0].WindowsDriverCandidates);
        Assert.Equal(1, matrix[5].WindowsDriverCandidates);

        var copied = string.Join(" ", home.Select(row =>
            row.Group + " " + row.DeviceStatus + " " + row.WindowsStatus));
        Assert.DoesNotContain("PRIVATE_", copied);
        Assert.DoesNotContain(@"PCI\", copied);
        Assert.DoesNotContain("DEV_", copied);
        Assert.DoesNotContain(guidNvidia, copied);
        Assert.DoesNotContain(guidIntel, copied);
        Assert.DoesNotContain(guidRealtek, copied);
        Assert.DoesNotContain("NVIDIA network utility", copied);
    }

    [Fact]
    public void PnpCode28AppearsAsMissingDriverWithoutInventingUpdates()
    {
        var nvidia = Nvidia with
        {
            ConfigManagerErrorCode = 28,
            HasDriver = false
        };
        var intel = Device("PRIVATE_NET", "Killer Wi-Fi 6E",
            "Net", @"PCI\VEN_8086&DEV_51F1") with
        {
            ConfigManagerErrorCode = 10
        };
        var realtek = Realtek with
        {
            HasDriver = false,
            DriverMetadataQuerySucceeded = false
            // ConfigManagerErrorCode=0: unavailable signed WMI metadata
            // is not proof of an uninstalled driver.
        };
        var rows = HomeVendorCoverageSummary.Create(
            [nvidia, intel, realtek],
            new DriverSearchReport([], [Pass("Completed")]), true);

        Assert.Contains("1 driver mancante (Windows codice 28)",
            rows[0].DeviceStatus);
        Assert.Contains("1 altro problema Windows",
            rows[1].DeviceStatus);
        Assert.Equal("1 dispositivo PnP classificato",
            rows[2].DeviceStatus);
        Assert.All(rows, row =>
        {
            Assert.Contains("0 candidati WUA per ID", row.WindowsStatus);
            Assert.False(row.IsInstallable);
        });

        var failed = HomeVendorCoverageSummary.Create(
            [nvidia, intel, realtek],
            new DriverSearchReport([], [Pass("Failed")]), true);
        Assert.Contains("1 driver mancante", failed[0].DeviceStatus);
        Assert.All(failed, row =>
        {
            Assert.Contains("Ricerca WUA non verificata", row.WindowsStatus);
            Assert.DoesNotContain("0 candidati", row.WindowsStatus);
        });

        var unverified = HomeVendorCoverageSummary.Create(
            [nvidia, intel, realtek], null, false);
        Assert.All(unverified, row =>
        {
            Assert.Equal("Inventario PnP non verificato", row.DeviceStatus);
            Assert.False(row.CanInspectLocalDrivers);
        });
    }

    [Fact]
    public void DuplicatePnpNodesDoNotCountWindowsProblemsTwice()
    {
        var broken = Nvidia with
        {
            ConfigManagerErrorCode = 28,
            HasDriver = false
        };
        var rows = HomeVendorCoverageSummary.Create(
            [broken, broken with { DeviceId = "private_gpu" }],
            new DriverSearchReport([], [Pass("Completed")]), true);
        Assert.Equal("1 dispositivo PnP classificato · " +
            "1 driver mancante (Windows codice 28)",
            rows[0].DeviceStatus);
        Assert.Equal("0 dispositivi PnP classificati",
            rows[1].DeviceStatus);
        Assert.Equal("0 dispositivi PnP classificati",
            rows[2].DeviceStatus);
    }


    [Fact]
    public void TruncatedWuaIdentifiersNeverClaimZeroCandidatesForDetectedVendors()
    {
        var guid = Guid.NewGuid().ToString("D");
        var uncorrelated = Update(guid, @"PCI\VEN_9999&DEV_0001") with
        {
            AdditionalHardwareIdsIncomplete = true
        };
        // Two WUA sources expose the same GUID, not two driver offers.
        var duplicate = uncorrelated with
        {
            Id = guid.ToUpperInvariant(),
            IsAdvancedCandidate = true
        };
        var report = new DriverSearchReport([uncorrelated, duplicate],
            [Pass("Completed")]);
        var rows = HomeVendorCoverageSummary.Create(
            [Nvidia, Killer, Realtek], report, true);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Contains("0 correlazioni WUA osservate", row.WindowsStatus);
            Assert.Contains("ID WUA incompleti", row.WindowsStatus);
            Assert.Contains("assenza non verificabile", row.WindowsStatus);
            Assert.DoesNotContain("0 candidati WUA", row.WindowsStatus);
            Assert.False(row.IsInstallable);
        });
        var copied = string.Join(" ", rows.Select(row => row.WindowsStatus));
        Assert.DoesNotContain(guid, copied);
        Assert.DoesNotContain("PRIVATE", copied);
        Assert.DoesNotContain(@"PCI\", copied);
    }

    [Fact]
    public void MatchedGuidRemainsOneObservedCandidateWithIncompleteIdWarning()
    {
        var guid = Guid.NewGuid().ToString("D");
        var first = Update(guid, Nvidia.HardwareIds[0]);
        var later = first with
        {
            Id = guid.ToUpperInvariant(),
            AdditionalHardwareIdsIncomplete = true,
            AdditionalHardwareIds = [Realtek.HardwareIds[0]]
        };
        var report = new DriverSearchReport([first, later],
            [Pass("Completed"), Pass("Failed")]);
        var rows = HomeVendorCoverageSummary.Create(
            [Nvidia, Killer, Realtek], report, true);

        Assert.Contains("1 candidato WUA per ID", rows[0].WindowsStatus);
        Assert.Contains("ID WUA incompleti", rows[0].WindowsStatus);
        Assert.Contains("fonti Windows parziali", rows[0].WindowsStatus);
        Assert.Contains("0 correlazioni WUA osservate", rows[1].WindowsStatus);
        Assert.Contains("assenza non verificabile", rows[1].WindowsStatus);
        Assert.Contains("1 candidato WUA per ID", rows[2].WindowsStatus);
        Assert.Contains("ID WUA incompleti", rows[2].WindowsStatus);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
    }

    [Fact]
    public void InvalidGuidCannotTriggerIncompleteWuaWarning()
    {
        var wrong = Update("PRIVATE-INVALID-GUID",
            @"PCI\VEN_9999&DEV_0001") with
        {
            AdditionalHardwareIdsIncomplete = true
        };
        var report = new DriverSearchReport([wrong], [Pass("Completed")]);
        var rows = HomeVendorCoverageSummary.Create([Nvidia], report, true);

        Assert.Contains("0 candidati WUA per ID", rows[0].WindowsStatus);
        Assert.DoesNotContain("ID WUA incompleti", rows[0].WindowsStatus);
        Assert.False(rows[0].IsInstallable);

        var failed = HomeVendorCoverageSummary.Create(
            [Nvidia], new DriverSearchReport([wrong],
                [Pass("Failed")]), true);
        Assert.Contains("ricerca WUA non verificata", failed[0].WindowsStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0 correlazioni", failed[0].WindowsStatus);
    }

}
