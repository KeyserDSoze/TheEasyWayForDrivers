using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsSetupApiRankEvidenceTests
{
    private static WindowsSetupApiRankEvidence.Driver Driver(
        uint rank, string description = "GeForce RTX Laptop GPU",
        string provider = "NVIDIA", ulong version = 0x00200000000F0001UL) =>
        new(rank, description, provider, version);

    [Fact]
    public void WindowsLocalRanksAreShownAscendingWithHexAndCorrectVersion()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
        [
            Driver(0x00FF2001, "Older driver", "NVIDIA"),
            Driver(0x00FF0001, "Preferred driver", "NVIDIA"),
            Driver(0x00FF1001, "Other driver", "Intel")
        ], true);

        Assert.Equal(WindowsSetupApiRankEvidence.State.Available, report.Status);
        Assert.False(report.Incomplete);
        Assert.Equal(3, report.DriversShown);
        Assert.Equal(3, report.DriversEnumerated);
        var low = report.DisplayText.IndexOf("0x00FF0001",
            StringComparison.Ordinal);
        var mid = report.DisplayText.IndexOf("0x00FF1001",
            StringComparison.Ordinal);
        var high = report.DisplayText.IndexOf("0x00FF2001",
            StringComparison.Ordinal);
        Assert.True(low < mid && mid < high);
        Assert.Contains("32.0.15.1", report.DisplayText);
        Assert.Contains("già presenti", report.DisplayText);
        Assert.Contains("non prova che esistano nuovi aggiornamenti",
            report.DisplayText);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void NativeDriverVersionUsesAllFourUnsigned16BitSegments()
    {
        Assert.Equal("65535.65534.1.0",
            WindowsSetupApiRankEvidence.FormatVersion(
                0xFFFFFFFE00010000UL));
        Assert.Equal("0.0.0.0",
            WindowsSetupApiRankEvidence.FormatVersion(0));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\4&SECRET-SERIAL", "[etichetta oscurata]")]
    [InlineData(@"C:\Users\SECRET\driver.inf", "[etichetta oscurata]")]
    [InlineData("https://vendor.example/driver", "[etichetta oscurata]")]
    [InlineData("USB VID_0BDA hidden", "[etichetta oscurata]")]
    [InlineData("Qualcomm / Atheros", "[etichetta oscurata]")]
    [InlineData("\u202E\u2066", "N/D")]
    public void DriverProvidedLabelsCannotExposeDeviceIdsPathsOrUnsafeChars(
        string input, string expected)
    {
        Assert.Equal(expected, WindowsSetupApiRankEvidence.SafeLabel(input));
    }

    [Fact]
    public void SafeLabelsCannotLeakIdentifiersFromNativeDriverText()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
        [
            Driver(0x00100000, @"USB\VID_1234&PID_4321\PRIVATE_UNIT",
                @"C:\PRIVATE\driver.inf")
        ], true);
        Assert.Contains("[etichetta oscurata]", report.DisplayText);
        Assert.DoesNotContain("PRIVATE", report.DisplayText);
        Assert.DoesNotContain(@"USB\", report.DisplayText);
        Assert.DoesNotContain(@"C:\", report.DisplayText);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void EmptyOrUnreadableWindowsDriverListNeverMeansNoUpdates()
    {
        var empty = WindowsSetupApiRankEvidence.Summarize([], true);
        var notVerified = WindowsSetupApiRankEvidence.NotVerified();
        Assert.Equal(WindowsSetupApiRankEvidence.State.NotVerified,
            empty.Status);
        Assert.Equal(notVerified, empty);
        Assert.Contains("non verificabile", empty.DisplayText);
        Assert.Contains("Nessuna conclusione", empty.DisplayText);
        Assert.True(empty.Incomplete);
        Assert.False(empty.IsInstallable);
    }

    [Fact]
    public void PartialWindowsEnumerationMustBeCalledPartial()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
            [Driver(0x00100000)], false);
        Assert.True(report.Incomplete);
        Assert.Contains("PARZIALE", report.DisplayText);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void OutputIsBoundedAndDoesNotIncludeAllNativeDriverLabels()
    {
        var source = Enumerable.Range(0,
            WindowsSetupApiRankEvidence.MaxEnumeratedDrivers)
            .Select(i => Driver((uint)(i + 1), "Device " + i))
            .ToArray();
        var report = WindowsSetupApiRankEvidence.Summarize(source, false);

        Assert.Equal(WindowsSetupApiRankEvidence.MaxDisplayedDrivers,
            report.DriversShown);
        Assert.Equal(WindowsSetupApiRankEvidence.MaxEnumeratedDrivers,
            report.DriversEnumerated);
        Assert.True(report.Incomplete);
        Assert.Contains("altre righe non mostrate", report.DisplayText);
        Assert.DoesNotContain("Device 255", report.DisplayText);
        Assert.Throws<InvalidDataException>(() =>
            WindowsSetupApiRankEvidence.Summarize(
                source.Append(Driver(0)).ToArray(), false));
    }

    [Fact]
    public void DriverDescriptionsAreCappedEvenWhenLongButStillValid()
    {
        var longName = new string('A', 300);
        var report = WindowsSetupApiRankEvidence.Summarize(
            [Driver(1, longName)], true);
        Assert.Contains(new string('A',
            WindowsSetupApiRankEvidence.MaxLabelCharacters), report.DisplayText);
        Assert.DoesNotContain(new string('A', 100), report.DisplayText);
        Assert.DoesNotContain(longName, report.DisplayText);
    }

    [Fact]
    public void RankingIsNotInferredFromInputOrderingOrOnlyFromVersion()
    {
        var bestRankOldVersion = Driver(0x00100000, "Old",
            "NVIDIA", 0x0001000000000000UL);
        var worseRankNewVersion = Driver(0x00FF0000, "New",
            "NVIDIA", 0x0063000000000000UL);
        var report = WindowsSetupApiRankEvidence.Summarize(
            [worseRankNewVersion, bestRankOldVersion], true);
        Assert.True(report.DisplayText.IndexOf("Old", StringComparison.Ordinal)
            < report.DisplayText.IndexOf("New", StringComparison.Ordinal));
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
    }

    [Fact]
    public void WindowsRunnerExportsRequiredSetupApiReadOnlyProcedures()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var library = NativeLibrary.Load("setupapi.dll");
        try
        {
            foreach (var name in new[]
            {
                "SetupDiGetClassDevsW",
                "SetupDiOpenDeviceInfoW",
                "SetupDiBuildDriverInfoList",
                "SetupDiEnumDriverInfoW",
                "SetupDiGetDriverInstallParamsW",
                "SetupDiDestroyDriverInfoList",
                "SetupDiDestroyDeviceInfoList"
            })
                Assert.True(NativeLibrary.TryGetExport(library, name, out _),
                    "Procedura SetupAPI Windows non disponibile: " + name);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }
}
