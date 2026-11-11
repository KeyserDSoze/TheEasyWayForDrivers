using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsSetupApiRankEvidenceTests
{
    private static WindowsSetupApiRankEvidence.Driver Driver(
        uint rank, string description = "GeForce RTX Laptop GPU",
        string provider = "NVIDIA", ulong version = 0x00200000000F0001UL,
        DateTimeOffset? date = null) =>
        new(rank, description, provider, version, date);

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
    public void EqualRankOrdersByNewerInfDateEvenWhenVersionIsLower()
    {
        var newerDate = new DateTimeOffset(2026, 11, 10, 0, 0, 0,
            TimeSpan.Zero);
        var olderDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0,
            TimeSpan.Zero);
        var report = WindowsSetupApiRankEvidence.Summarize(
        [
            Driver(0x00FF0000, "Old date but version 99", "AAA",
                0x0063000000000000UL, olderDate),
            Driver(0x00FF0000, "New date but version 1", "ZZZ",
                0x0001000000000000UL, newerDate)
        ], true);

        Assert.True(report.DisplayText.IndexOf("New date",
            StringComparison.Ordinal) < report.DisplayText.IndexOf(
                "Old date", StringComparison.Ordinal));
        Assert.Contains("data INF 2026-11-10", report.DisplayText);
        Assert.Contains("data INF 2025-01-01", report.DisplayText);
        Assert.Contains("data INF DriverVer", report.DisplayText);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
    }

    [Fact]
    public void EqualRankAndDateOrderByVersionBeforeAlphabeticalName()
    {
        var date = new DateTimeOffset(2026, 8, 1, 0, 0, 0,
            TimeSpan.Zero);
        var report = WindowsSetupApiRankEvidence.Summarize(
        [
            Driver(0x00FF0000, "AAA old version", "AAA",
                0x0001000000000000UL, date),
            Driver(0x00FF0000, "ZZZ newer version", "ZZZ",
                0x0002000000000000UL, date)
        ], true);

        Assert.True(report.DisplayText.IndexOf("ZZZ newer version",
            StringComparison.Ordinal) < report.DisplayText.IndexOf(
                "AAA old version", StringComparison.Ordinal));
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void LowerRankAlwaysWinsDisplayOrderEvenWithOlderDate()
    {
        var old = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var recent = new DateTimeOffset(2026, 11, 1, 0, 0, 0,
            TimeSpan.Zero);
        var report = WindowsSetupApiRankEvidence.Summarize(
        [
            Driver(0x00FF0001, "New but weaker rank", "NVIDIA",
                0x0063000000000000UL, recent),
            Driver(0x000F0000, "Old better rank", "NVIDIA",
                0x0001000000000000UL, old)
        ], true);
        Assert.True(report.DisplayText.IndexOf("Old better rank",
            StringComparison.Ordinal) < report.DisplayText.IndexOf(
                "New but weaker rank", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownNativeDateIsNotFabricatedAsWindowsSelection()
    {
        var known = Driver(0x00FF0000, "Z verified date", "ZZZ",
            date: new DateTimeOffset(2026, 1, 1, 0, 0, 0,
                TimeSpan.Zero));
        var unknown = Driver(0x00FF0000, "A unavailable date", "AAA");
        var report = WindowsSetupApiRankEvidence.Summarize(
            [unknown, known], true);

        Assert.Contains("data INF N/D", report.DisplayText);
        Assert.Contains("data INF 2026-01-01", report.DisplayText);
        Assert.Contains("rank uguali con data INF non verificata",
            report.DisplayText);
        Assert.Contains("NON prova la preferenza di Windows",
            report.DisplayText);
        Assert.True(report.DisplayText.IndexOf("Z verified date",
            StringComparison.Ordinal) < report.DisplayText.IndexOf(
                "A unavailable date", StringComparison.Ordinal));
        // Reading nodes succeeded; the missing optional date alone does
        // not pretend the entire native SetupAPI enumeration failed.
        Assert.False(report.Incomplete);
    }

    [Fact]
    public void NativeFileTimeConversionRejectsInvalidOrZeroDates()
    {
        var utc = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTimeOffset(utc),
            WindowsSetupApiRankEvidence.FromNativeFileTime(
                utc.ToFileTimeUtc()));
        Assert.Null(WindowsSetupApiRankEvidence.FromNativeFileTime(0));
        Assert.Null(WindowsSetupApiRankEvidence.FromNativeFileTime(-1));
        Assert.Null(WindowsSetupApiRankEvidence.FromNativeFileTime(
            long.MaxValue));
    }

    [Fact]
    public void DateEnrichmentCannotDisplayPnpIdsPathsOrClaimOnlineUpdate()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
        [
            Driver(0x00FF0000, @"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL",
                @"C:\Users\PRIVATE_USER\oem12.inf",
                date: new DateTimeOffset(2026, 1, 1, 0, 0, 0,
                    TimeSpan.Zero))
        ], true);
        Assert.Contains("[etichetta oscurata]", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_SERIAL", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_USER", report.DisplayText);
        Assert.Contains("data INF 2026-01-01", report.DisplayText);
        Assert.False(report.IsInstallable);
        Assert.False(report.HasVerifiedExternalOemRanking);
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
