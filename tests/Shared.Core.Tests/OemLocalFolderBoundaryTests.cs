using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #531: a leaf directory can be ordinary while an ancestor is a link
/// redirecting a read-only OEM INF/CAT verification elsewhere.
/// </summary>
public sealed class OemLocalFolderBoundaryTests
{
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "Omega-Oem-FolderBoundary-" + Guid.NewGuid().ToString("N"));

        public Scratch() => Directory.CreateDirectory(Root);
        public string PathOf(params string[] parts) =>
            Path.Combine([Root, .. parts]);

        public void Dispose()
        {
            var link = PathOf("redirect");
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void PlainFolderCanBeReadAndReturnedWithoutMutation()
    {
        using var scratch = new Scratch();
        var directory = scratch.PathOf("vendor", "graphics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "driver.inf"), "fixture");

        var result = OemLocalFolderBoundary.RequirePlainLocalDirectory(
            directory);

        Assert.Equal(Path.GetFullPath(directory), result.FullName);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsSymlinkAncestorEvenIfSelectedLeafIsOrdinary(
        bool nested)
    {
        using var scratch = new Scratch();
        var outside = scratch.PathOf("outside");
        Directory.CreateDirectory(Path.Combine(outside, "graphics", "device"));
        Directory.CreateSymbolicLink(scratch.PathOf("redirect"), outside);
        var selected = nested
            ? scratch.PathOf("redirect", "graphics", "device")
            : scratch.PathOf("redirect", "graphics");

        var error = Assert.Throws<InvalidDataException>(() =>
            OemLocalFolderBoundary.RequirePlainLocalDirectory(selected));
        Assert.Contains("collegamento", error.Message);
        Assert.DoesNotContain(scratch.Root, error.Message);
        Assert.True(Directory.Exists(selected)); // child is real through link
    }

    [Fact]
    public void MissingRootIsNotAnApprovedLocalAuditDirectory()
    {
        using var scratch = new Scratch();
        Assert.Throws<InvalidDataException>(() =>
            OemLocalFolderBoundary.RequirePlainLocalDirectory(
                scratch.PathOf("missing", "drivers")));
        Assert.False(Directory.Exists(scratch.PathOf("missing")));
    }

    [Fact]
    public void DirectLinkCannotBeAnAuditRoot()
    {
        using var scratch = new Scratch();
        var outside = scratch.PathOf("outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(scratch.PathOf("redirect"), outside);
        Assert.Throws<InvalidDataException>(() =>
            OemLocalFolderBoundary.RequirePlainLocalDirectory(
                scratch.PathOf("redirect")));
    }
}
