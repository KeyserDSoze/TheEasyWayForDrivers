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
    public void MoreThan96InfFilesFailsClosed()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i <= OemZipInfEvidence.MaxInfFiles; i++)
            folder.Add($"drivers/driver{i:D3}.inf", Inf(NvidiaGpu));
        Assert.Throws<InvalidDataException>(() => Compare(folder.Path));
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
