using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #528: reject existing staging junctions BEFORE CreateDirectory can
/// create folders through them. These tests never run an installer.
/// </summary>
public sealed class SetupPayloadPrecreateLinkTests
{
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "OmegaSetup-LinkPreflight-" + Guid.NewGuid().ToString("N"));

        public Scratch() => Directory.CreateDirectory(Root);

        public string Subpath(params string[] parts) =>
            Path.Combine([Root, .. parts]);

        public void Dispose()
        {
            // Remove the test link itself before recursively cleaning up.
            // The real target must never be removed through the link.
            foreach (var link in new[] { Subpath("redirect"),
                         Subpath("stage", "Service", "runtime") })
                if (Directory.Exists(link))
                    Directory.Delete(link);
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static MemoryStream Zip(params string[] extra)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var name in new[]
            {
                "Service/App1.Service.exe",
                "Desktop/App2.Desktop.exe"
            }.Concat(extra))
            {
                var entry = zip.CreateEntry(name);
                if (name.EndsWith('/'))
                    continue;
                using var output = entry.Open();
                output.Write(Encoding.UTF8.GetBytes("synthetic"));
            }
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void MissingDestinationUnderExistingLinkDoesNotCreateExternalFolder()
    {
        using var scratch = new Scratch();
        var outside = scratch.Subpath("outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(scratch.Subpath("redirect"), outside);
        var destination = scratch.Subpath("redirect", "new-stage");
        using var zip = Zip();

        var error = Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, destination));

        Assert.Contains("collegamento", error.Message);
        Assert.DoesNotContain(scratch.Root, error.Message);
        Assert.False(Directory.Exists(Path.Combine(outside, "new-stage")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Theory]
    [InlineData("Service/runtime/deeper/")]
    [InlineData("Service/runtime/deeper/driver.dll")]
    public void NestedLinkCannotCreateExternalDirectoryBeforeReparseCheck(
        string zipEntry)
    {
        using var scratch = new Scratch();
        var outside = scratch.Subpath("outside");
        var stage = scratch.Subpath("stage");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(scratch.Subpath("stage", "Service"));
        Directory.CreateSymbolicLink(
            scratch.Subpath("stage", "Service", "runtime"), outside);
        using var zip = Zip(zipEntry);

        var error = Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, stage));

        Assert.Contains("collegamento", error.Message);
        Assert.False(Directory.Exists(Path.Combine(outside, "deeper")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public void AbsentOrdinaryParentsStillMaterializeForLegitimatePayload()
    {
        using var scratch = new Scratch();
        using var zip = Zip("Service/runtime/deeper/driver.dll");
        var stage = scratch.Subpath("new-stage", "nested");

        SetupPayloadArchive.Extract(zip, stage);

        Assert.Equal("synthetic", File.ReadAllText(Path.Combine(
            stage, "Service", "runtime", "deeper", "driver.dll")));
        Assert.True(File.Exists(Path.Combine(
            stage, "Desktop", "App2.Desktop.exe")));
    }
}
