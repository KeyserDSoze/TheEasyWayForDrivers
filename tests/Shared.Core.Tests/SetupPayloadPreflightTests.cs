using System.Diagnostics;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SetupPayloadPreflightTests
{
    private static readonly string FixtureAssembly =
        typeof(SetupPayloadPreflight).Assembly.Location;

    private static string FixtureVersion
    {
        get
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(
                FixtureAssembly).FileVersion;
            Assert.True(Version.TryParse(fileVersion, out var parsed));
            return $"{parsed!.Major}.{parsed.Minor}.{Math.Max(parsed.Build, 0)}";
        }
    }

    private sealed class TempPayload : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "OmegaSetupPreflight-" + Guid.NewGuid().ToString("N"));

        public string Component(string subdirectory, string filename)
        {
            var directory = System.IO.Path.Combine(Path, subdirectory);
            Directory.CreateDirectory(directory);
            return System.IO.Path.Combine(directory, filename);
        }

        public string CopyService()
        {
            var path = Component("Service", "App1.Service.exe");
            File.Copy(FixtureAssembly, path);
            return path;
        }

        public string CopyDesktop()
        {
            var path = Component("Desktop", "App2.Desktop.exe");
            File.Copy(FixtureAssembly, path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void MatchingPeVersionsPassBeforeSetupTouchesWindows()
    {
        using var payload = new TempPayload();
        var service = payload.CopyService();
        var desktop = payload.CopyDesktop();

        SetupPayloadPreflight.Validate(payload.Path, FixtureVersion);

        Assert.Equal(new FileInfo(service).Length,
            new FileInfo(desktop).Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingComponentFailsBeforeAnyServiceStop(bool missingService)
    {
        using var payload = new TempPayload();
        if (missingService)
            payload.CopyDesktop();
        else
            payload.CopyService();

        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadPreflight.Validate(payload.Path, FixtureVersion));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyComponentIsRejected(bool emptyService)
    {
        using var payload = new TempPayload();
        payload.CopyService();
        payload.CopyDesktop();
        var path = emptyService
            ? payload.Component("Service", "App1.Service.exe")
            : payload.Component("Desktop", "App2.Desktop.exe");
        File.WriteAllBytes(path, []);

        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadPreflight.Validate(payload.Path, FixtureVersion));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnversionedComponentCannotImpersonateSetup(bool badService)
    {
        using var payload = new TempPayload();
        payload.CopyService();
        payload.CopyDesktop();
        var path = badService
            ? payload.Component("Service", "App1.Service.exe")
            : payload.Component("Desktop", "App2.Desktop.exe");
        File.WriteAllText(path, "not a versioned PE");

        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadPreflight.Validate(payload.Path, FixtureVersion));
    }

    [Fact]
    public void PayloadWithStaleMajorMinorBuildIsRejected()
    {
        using var payload = new TempPayload();
        payload.CopyService();
        payload.CopyDesktop();
        var v = Version.Parse(FixtureVersion);
        var wrongSetupVersion = $"{v.Major}.{v.Minor}.{v.Build + 1}";

        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadPreflight.Validate(payload.Path, wrongSetupVersion));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.0")]
    [InlineData("0.0.1.0")]
    [InlineData("not-a-version")]
    public void InvalidOrNoncanonicalSetupVersionFailsClosed(string version)
    {
        using var payload = new TempPayload();
        payload.CopyService();
        payload.CopyDesktop();
        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadPreflight.Validate(payload.Path, version));
    }

    [Fact]
    public void PreflightDoesNotWriteOrReplaceAnyStagedExecutable()
    {
        using var payload = new TempPayload();
        var service = payload.CopyService();
        var desktop = payload.CopyDesktop();
        var beforeService = File.ReadAllBytes(service);
        var beforeDesktop = File.ReadAllBytes(desktop);

        SetupPayloadPreflight.Validate(payload.Path, FixtureVersion);

        Assert.Equal(beforeService, File.ReadAllBytes(service));
        Assert.Equal(beforeDesktop, File.ReadAllBytes(desktop));
    }
}
