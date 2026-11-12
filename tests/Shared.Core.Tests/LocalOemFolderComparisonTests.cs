using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LocalOemFolderComparisonTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string KillerWifi = @"PCI\VEN_8086&DEV_51F1";

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } =
            Directory.CreateTempSubdirectory("OmegaDrive-OemFolderTest-").FullName;

        public string Add(string name, string contents)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents, new UTF8Encoding(false));
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static DriverInfo Device(string name, string id, string version) =>
        new("PRIVATE_INSTANCE_" + name, name, "OEM", "System", "OEM",
            version, null, "oem15.inf", true, true, 0, [id], []);

    private static string Inf(string id, string version = "32.0.15.7000") =>
        $"""
        [Version]
        Signature="$Windows NT$"
        Class=System
        DriverVer=10/04/2026,{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        %Device%=Install,{id}
        """;

    private static LocalOemDriverComparison.Report Compare(
        string root, IReadOnlyList<DriverInfo>? devices = null,
        CancellationToken cancellationToken = default) =>
        LocalOemFolderComparison.Compare(
            root,
            devices ?? [Device("RTX 4060 Laptop GPU", NvidiaGpu, "31.0.15.5222")],
            "AMD64", 10, 0, 26100, cancellationToken);

    [Fact]
    public void MatchesMultipleRealDevicesAcrossSubdirectoriesWithoutExecutingSetup()
    {
        using var folder = new TemporaryFolder();
        folder.Add("nvidia/display/driver.inf", Inf(NvidiaGpu));
        folder.Add("nvidia/audio/driver.inf", Inf(NvidiaAudio, "2.1.0.0"));
        folder.Add("intel/wifi.inf", Inf(KillerWifi, "24.0.0.0"));
        folder.Add("setup.exe", "DO NOT RUN ME");
        folder.Add("nvidia/display/nvlddmkm.sys", "NOT A SCRIPT");

        var report = Compare(folder.Path,
        [
            Device("RTX 4060 Laptop GPU", NvidiaGpu, "31.0.15.5222"),
            Device("NVIDIA HD Audio", NvidiaAudio, "1.0.0.0"),
            Device("Killer WiFi", KillerWifi, "23.0.1.1")
        ]);
        Assert.Equal(3, report.InfExamined);
        Assert.Equal(3, report.DevicesWithEvidence);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.All(report.Findings, finding =>
            Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
                finding.PlatformVerdict));
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.Contains("Nessun EXE", string.Join(" ", report.Notes));
        Assert.DoesNotContain("PRIVATE_INSTANCE", report.Summary);
        Assert.Equal("DO NOT RUN ME", File.ReadAllText(
            System.IO.Path.Combine(folder.Path, "setup.exe")));
    }

    [Fact]
    public void PersonalInfFilenamesAreNotCopiedToPreviewOrReport()
    {
        using var folder = new TemporaryFolder();
        // Sanitizing names to [A-Za-z0-9_] would preserve all of these
        // private order/user identifiers. No file path should survive.
        folder.Add("PRIVATE_ACCOUNT_8174/PRIVATE_ORDER_98234_GPU.inf",
            Inf(NvidiaGpu));
        folder.Add("PRIVATE_CUSTOMER_33/PRIVATE_ORDER_98234_GPU.inf",
            Inf(KillerWifi, "24.0.0.0"));

        var report = Compare(folder.Path,
        [
            Device("RTX 4060 Laptop GPU", NvidiaGpu, "31.0.15.5222"),
            Device("Intel Killer WiFi", KillerWifi, "23.0.1.1")
        ]);

        Assert.Equal(2, report.InfExamined);
        Assert.Equal(2, report.TotalMatchedDevices);
        Assert.Equal(2, report.PotentialNewerDevices);
        Assert.All(report.Findings, finding =>
            Assert.Matches(@"^inf\d{4}\.inf$", finding.InfName));
        Assert.Equal(2, report.Findings.Select(f => f.InfName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("PRIVATE_ORDER", serialized);
        Assert.DoesNotContain("PRIVATE_ACCOUNT", serialized);
        Assert.DoesNotContain("PRIVATE_CUSTOMER", serialized);
        Assert.DoesNotContain("98234", serialized);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public void GeneratedInfNamesStayOpaqueAndUniqueAcrossBatchBoundary()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i <= OemZipInfEvidence.MaxInfFiles; i++)
            folder.Add($"PRIVATE_ORDER_45123/PRIVATE_USER_{i:D3}.inf",
                Inf($@"PCI\VEN_9999&DEV_{i:X4}", "1.0.0.0"));
        folder.Add("PRIVATE_ORDER_45123/PRIVATE_USER_TARGET_GPU.inf",
            Inf(NvidiaGpu));

        var report = Compare(folder.Path);

        Assert.Equal(OemZipInfEvidence.MaxInfFiles + 2, report.InfExamined);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.True(Assert.Single(report.Findings).IsPotentialNewer);
        // The cross-batch summarizer intentionally replaces the generated
        // per-INF label with a generic "Archivio NN" source label.
        // Neither presentation can contain the original private filename.
        Assert.StartsWith("Archivio ",
            Assert.Single(report.Findings).InfName);
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("PRIVATE_ORDER", serialized);
        Assert.DoesNotContain("PRIVATE_USER", serialized);
        Assert.DoesNotContain("45123", serialized);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void ExtractedFolderSharesUtf16BeAndAnsiDecodingWithZipAndCab()
    {
        using var folder = new TemporaryFolder();
        const string gpuId = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        const string wifiId = @"PCI\VEN_8086&DEV_51F1";
        var gpu = Inf(gpuId);
        var unicode = new UnicodeEncoding(true, true, true);
        File.WriteAllBytes(folder.Add("nvidia/display.inf", ""), [
            .. unicode.GetPreamble(), .. unicode.GetBytes(gpu)
        ]);

        var wifi = Inf(wifiId, "24.0.0.0")
            .Replace("Class=System", "Class=System\n; OEM " + '\u00E9');
        File.WriteAllBytes(folder.Add("intel/wifi.inf", ""),
            Encoding.Latin1.GetBytes(wifi));

        var report = Compare(folder.Path, [
            Device("RTX 4060 Laptop GPU", gpuId, "31.0.15.5222"),
            Device("Killer Wi-Fi", wifiId, "23.0.1.1")
        ]);
        Assert.Equal(2, report.InfExamined);
        Assert.Equal(2, report.DevicesWithEvidence);
        Assert.Equal(2, report.PotentialNewerDevices);
        Assert.Equal(1, report.LegacyAnsiCount);
        Assert.Equal(0, report.RejectedInfCount);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void MalformedInfInFolderIsExplicitlyCountedNotAFalseZero()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllBytes(folder.Add("bad.inf", ""),
            [0xFF, 0xFE, 0x00, 0xD8]);
        var report = Compare(folder.Path);
        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.RejectedInfCount);
        Assert.Equal(0, report.DevicesWithEvidence);
        Assert.Contains("1 scartati", report.Summary);
        Assert.Contains("parziale", string.Join(" ", report.Notes));
    }

    [Fact]
    public void DuplicateHardwareIdInDifferentInfsCountsOnlyOneDevice()
    {
        using var folder = new TemporaryFolder();
        folder.Add("graphics/old.inf", Inf(NvidiaGpu, "31.0.15.1000"));
        folder.Add("graphics/new.inf", Inf(NvidiaGpu, "32.0.15.7000"));
        var report = Compare(folder.Path);
        Assert.Equal(2, report.InfExamined);
        Assert.Single(report.Findings);
        Assert.Equal(1, report.AdditionalMatchesOmitted);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void UnrelatedInfIsNotAConfirmedUpdate()
    {
        using var folder = new TemporaryFolder();
        folder.Add("driver.inf", Inf(KillerWifi));
        var report = Compare(folder.Path);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Equal(1, report.InfExamined);
    }

    [Fact]
    public void DirectoryWithOnlyExeDoesNotClaimWindowsHasNoDriverUpdates()
    {
        using var folder = new TemporaryFolder();
        folder.Add("vendor/setup.exe", "EXE stays unopened");
        var report = Compare(folder.Path);
        Assert.Equal(0, report.InfExamined);
        Assert.Empty(report.Findings);
        Assert.Contains("Nessun INF presente", report.Notes[0]);
        Assert.Contains("Non significa", report.Notes[0]);
    }

    [Fact]
    public void MoreThan96InfFilesNowCorrelateNvidiaKillerAndRealtekAcrossBatches()
    {
        using var folder = new TemporaryFolder();
        const string realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        for (var i = 0; i < 150; i++)
            folder.Add($"drivers/decoy{i:D3}.inf",
                Inf($@"PCI\VEN_9999&DEV_{i:X4}", "1.0.0.0"));

        folder.Add("nvidia/old.inf", Inf(NvidiaGpu, "30.0.0.0"));
        folder.Add("nvidia/new.inf", Inf(NvidiaGpu, "32.0.15.7000"));
        folder.Add("intel/killer.inf", Inf(KillerWifi, "24.0.0.0"));
        folder.Add("audio/realtek.inf", Inf(realtek, "6.0.0.0"));

        var report = Compare(folder.Path,
        [
            Device("RTX 4060 Laptop GPU", NvidiaGpu, "31.0.15.5222"),
            Device("Killer WiFi", KillerWifi, "23.0.1.1"),
            Device("Realtek Audio", realtek, "5.0.0.0")
        ]);

        Assert.Equal(154, report.InfExamined);
        Assert.Equal(3, report.TotalMatchedDevices);
        Assert.Equal(3, report.DevicesWithEvidence);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.Equal(3, report.MatchedDeviceOrdinals!.Count);
        Assert.Equal(154, report.InfWithDeclaredIds);
        Assert.Equal(0, report.RejectedInfCount);
        Assert.Contains(report.Findings,
            finding => finding.DeviceName == "RTX 4060 Laptop GPU" &&
                finding.PackageVersion == "32.0.15.7000");
        Assert.Contains("lotti INF", string.Join(" ", report.Notes));
        Assert.True(report.AdditionalMatchesOmitted >= 1);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_INSTANCE", report.Summary);
    }

    [Fact]
    public void FirstMatchedInfAfterThe96thFileStillContributesEvidence()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i < OemZipInfEvidence.MaxInfFiles; i++)
            folder.Add($"drivers/irrelevant{i:D3}.inf",
                Inf($@"PCI\VEN_9999&DEV_{i:X4}"));
        folder.Add("drivers/killer.inf", Inf(KillerWifi, "24.0.0.0"));

        var report = Compare(folder.Path,
            [Device("Killer Wi-Fi", KillerWifi, "23.0.1.0")]);
        Assert.Equal(97, report.InfExamined);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.Single(report.Findings);
        Assert.True(report.Findings[0].IsPotentialNewer);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void MalformedInfInLaterBatchDoesNotEraseOtherMatchingDrivers()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i < 100; i++)
            folder.Add($"drivers/irrelevant{i:D3}.inf",
                Inf($@"PCI\VEN_9999&DEV_{i:X4}"));
        folder.Add("nvidia/gpu.inf", Inf(NvidiaGpu));
        var corrupt = folder.Add("intel/corrupt.inf", "");
        File.WriteAllBytes(corrupt, [0xFF, 0xFE, 0x00, 0xD8]);

        var report = Compare(folder.Path);
        Assert.Equal(102, report.InfExamined);
        Assert.Equal(1, report.RejectedInfCount);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.False(report.IsInstallable);
        Assert.Contains("parziale", string.Join(" ", report.Notes));
    }

    [Fact]
    public void MoreThan384InfFilesFailsClosed()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i <= LocalOemFolderComparison.MaxFolderInfFiles; i++)
            folder.Add($"drivers/driver{i:D3}.inf", Inf(NvidiaGpu));
        Assert.Throws<InvalidDataException>(() => Compare(folder.Path));
    }

    [Fact]
    public void FourBatchesKeepEightyRowDisplayCapAndTrueDistinctCount()
    {
        using var folder = new TemporaryFolder();
        var devices = new List<DriverInfo>();
        for (var i = 0; i < 102; i++)
        {
            var id = $@"PCI\VEN_1234&DEV_{i:X4}";
            devices.Add(Device($"PCI Device {i}", id, "1.0.0.0"));
            folder.Add($"drivers/driver{i:D3}.inf", Inf(id, "2.0.0.0"));
        }

        var report = Compare(folder.Path, devices);
        Assert.Equal(102, report.InfExamined);
        Assert.Equal(102, report.TotalMatchedDevices);
        Assert.Equal(80, report.DevicesWithEvidence);
        Assert.Equal(22, report.MatchingDevicesOmitted);
        Assert.Equal(102, report.MatchedDeviceOrdinals!.Count);
        Assert.Equal(102, report.PnpMatchedDeviceOrdinals!.Count);
        var portalEvidence = PcSpecialistPackageReconciliation.LocalSource
            .FromArchive("W11 Intel Chipset.zip", report);
        Assert.Equal(102, portalEvidence.PnpMatches);
        Assert.Equal(0, portalEvidence.ReviewOnlyMatches);
        Assert.False(report.IsInstallable);
        Assert.DoesNotContain("PRIVATE_INSTANCE", report.Summary);
    }

    [Fact]
    public void OversizedInfIsRejectedBeforeReadingContent()
    {
        using var folder = new TemporaryFolder();
        folder.Add("huge.inf", new string('x', OemZipInfEvidence.MaxInfBytes + 1));
        Assert.Throws<InvalidDataException>(() => Compare(folder.Path));
    }

    [Fact]
    public void TooManyNestedFoldersAreRejected()
    {
        using var folder = new TemporaryFolder();
        var at = folder.Path;
        for (int i = 0; i <= LocalOemFolderComparison.MaxDepth; i++)
        {
            at = System.IO.Path.Combine(at, "nested");
            Directory.CreateDirectory(at);
        }
        Assert.Throws<InvalidDataException>(() => Compare(folder.Path));
    }

    [Fact]
    public void TooManySiblingDirectoriesFailBeforeUnboundedTraversal()
    {
        using var folder = new TemporaryFolder();
        for (int i = 0; i <= LocalOemFolderComparison.MaxDirectories; i++)
            Directory.CreateDirectory(System.IO.Path.Combine(
                folder.Path, $"driver-set-{i:D4}"));
        Assert.Throws<InvalidDataException>(() => Compare(folder.Path));
    }

    [Fact]
    public void PrecancelledRequestCannotReadInf()
    {
        using var folder = new TemporaryFolder();
        folder.Add("driver.inf", Inf(NvidiaGpu));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            Compare(folder.Path, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void NonexistentFolderIsNotInterpretedAsEmptyResult()
    {
        using var folder = new TemporaryFolder();
        Assert.Throws<InvalidDataException>(() =>
            Compare(System.IO.Path.Combine(folder.Path, "missing")));
    }

    [Fact]
    public void SymbolicDirectoryIsBlockedWhenFilesystemSupportsLinks()
    {
        using var folder = new TemporaryFolder();
        folder.Add("drivers/gpu.inf", Inf(NvidiaGpu));
        var link = System.IO.Path.Combine(folder.Path, "linked-driver-folder");
        try
        {
            Directory.CreateSymbolicLink(link,
                System.IO.Path.Combine(folder.Path, "drivers"));
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or IOException or
            PlatformNotSupportedException)
        {
            return; // Windows CI may not have symlink privilege.
        }

        Assert.Throws<InvalidDataException>(() => Compare(folder.Path));
    }
}
