using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #536: Discovery+ consumes WUA updates without trusting direct legacy
/// IPC titles, private paths/IDs, or unknown source metadata.
/// </summary>
public sealed class DiscoveryWuaAdapterPrivacyAndProvenanceTests
{
    private const string GuidA = "00000000-0000-4000-8000-000000000536";
    private const string GuidB = "00000000-0000-4000-8000-000000000537";
    private const string GpuId = @"PCI\VEN_10DE&DEV_28A0";
    private const string KillerId =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string AudioId =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverUpdateInfo Update(string id = GuidA,
        string source = "Windows Update online", string title = "NVIDIA GPU driver",
        string version = "31.0.16.6220", string? hardware = GpuId,
        IReadOnlyList<string>? extras = null) =>
        new(id, title, null, "Display", "Microsoft", version, null,
            false, "Microsoft", null, hardware, null,
            SearchSource: source, AdditionalHardwareIds: extras);

    private static DriverInfo Device(string kind, string id) =>
        new("PRIVATE_LOCAL_DEVICE", "Generic Windows device", "Microsoft",
            kind, "Microsoft", "1.0", null, null, true, true, 0, [id], []);

    [Fact]
    public void UnknownOriginsAndMalformedGuidsNeverBecomeWindowsUpdateResults()
    {
        var rows = DiscoveryResultAdapter.FromWindowsUpdate(
        [
            Update(id: "PRIVATE_LOCAL_ID"),
            Update(source: "PRIVATE_OEM_MIRROR"),
            Update(id: GuidB, source: "Windows Update online"),
            Update(id: GuidA, source: "Windows configurato")
        ]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(DiscoveryResultKind.WindowsUpdateDriver, row.Kind);
            Assert.Equal("Windows Update", row.Source);
            Assert.DoesNotContain("PRIVATE", row.Identifier);
        });
        Assert.Contains(rows, row => row.Identifier == GuidA);
        Assert.Contains(rows, row => row.Identifier == GuidB);
    }

    [Theory]
    [InlineData("NVIDIA driver CUSTOMER_ORDER123")]
    [InlineData("Intel driver SECRET_KEY123")]
    [InlineData("Realtek driver PRIVATE_USER123")]
    [InlineData("NVIDIA driver owner@example.invalid")]
    [InlineData(@"C:\Users\PRIVATE_ACCOUNT\gpu.inf")]
    [InlineData("Intel GPU\u200Bdriver")]
    public void UnsafeWuaTitlesRemainAsRedactedDisplayOnlyEntries(
        string title)
    {
        var row = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(title: title)]));
        Assert.Equal("Titolo driver non verificabile", row.Title);
        Assert.Equal(GuidA, row.Identifier);
        Assert.DoesNotContain(title, row.Title + row.Evidence);
        Assert.DoesNotContain("PRIVATE_", row.Title + row.Evidence);
        Assert.DoesNotContain("CUSTOMER_", row.Title + row.Evidence);
        Assert.Equal(GpuId, row.HardwareId);
        Assert.True(DiscoveryDeviceEvidence.IsDeviceCandidate(row,
            Device("Display", GpuId)));
    }

    [Theory]
    [InlineData("C:/Users/private/driver.inf")]
    [InlineData("32.0_SECRET_KEY")]
    [InlineData("3.0 OWNER@example.com")]
    [InlineData("32.0\u200B")]
    [InlineData("PRIVATE_PC")]
    public void InvalidVersionDoesNotAppearInDiscoveryResult(string version)
    {
        var row = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(version: version)]));
        Assert.Equal("N/D", row.Version);
        Assert.DoesNotContain(version, row.Version + row.Evidence);
    }

    [Fact]
    public void InvalidPrivateInstancePrimaryIsOmittedWithoutLosingValidExtras()
    {
        var privateHardware = GpuId + @"\PRIVATE_SERIAL_123456";
        var result = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(hardware: privateHardware,
                extras: [KillerId, @"C:\Users\someone\driver.inf",
                    AudioId, KillerId])]));
        Assert.Null(result.HardwareId);
        Assert.True(result.WuaIdsIncomplete);
        Assert.Equal([KillerId, AudioId], result.AdditionalHardwareIds);
        Assert.Contains("ID principale non verificato", result.Evidence);
        Assert.Contains("2 ID aggiuntivi validati", result.Evidence);
        Assert.DoesNotContain("PRIVATE_SERIAL", result.Evidence);
        Assert.DoesNotContain("someone", result.Evidence);
        Assert.True(DiscoveryDeviceEvidence.IsDeviceCandidate(result,
            Device("Net", KillerId)));
        Assert.True(DiscoveryDeviceEvidence.IsDeviceCandidate(result,
            Device("MEDIA", AudioId)));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(result,
            Device("Display", GpuId)));
        Assert.Contains("assenza non verificabile", result.Evidence);
    }

    [Fact]
    public void ValidWuaIdsArePreservedWithoutInventingIncompleteness()
    {
        var result = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(extras: [KillerId, AudioId])]));
        Assert.Equal("NVIDIA GPU driver", result.Title);
        Assert.Equal("31.0.16.6220", result.Version);
        Assert.Equal(GpuId, result.HardwareId);
        Assert.Equal([KillerId, AudioId], result.AdditionalHardwareIds);
        Assert.False(result.WuaIdsIncomplete);
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(result,
            Device("Display", GpuId)));
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(result,
            Device("Net", KillerId)));
    }

    [Fact]
    public void AdvancedWuaSourceAllowedOnlyWithAdvancedFlag()
    {
        const string source = "Windows Update online · avanzato";
        var advanced = Update(source: source) with
        {
            IsAdvancedCandidate = true
        };
        var rows = DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(source: source), advanced]);
        var result = Assert.Single(rows);
        Assert.Equal(GuidA, result.Identifier);
        Assert.Equal(DiscoveryResultKind.WindowsUpdateDriver, result.Kind);
        Assert.True(WindowsDriverCandidateMerge.IsKnownWuaGuid(advanced));
    }

    [Fact]
    public void OverBudgetExtrasAreBoundedAndReportedAsIncomplete()
    {
        var more = Enumerable.Range(1,
            WuaDriverEntryLimits.MaximumAdditionalIds + 1)
            .Select(i => $@"PCI\VEN_8086&DEV_{i:X4}").ToArray();
        var result = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate(
            [Update(extras: more)]));
        Assert.True(result.WuaIdsIncomplete);
        Assert.NotNull(result.AdditionalHardwareIds);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            result.AdditionalHardwareIds.Count);
        Assert.Contains("incompleto", result.Evidence);
        Assert.DoesNotContain("PRIVATE_", result.Evidence);
    }
}
