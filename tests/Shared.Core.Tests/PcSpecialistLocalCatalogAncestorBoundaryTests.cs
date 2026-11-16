using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #538: a PCSpecialist OEM bundle root can be an ordinary child BELOW
/// a preexisting junction; checking only the selected leaf misses it.
/// The planner is read-only and must reject such roots before enumeration.
/// </summary>
public sealed class PcSpecialistLocalCatalogAncestorBoundaryTests
{
    [Fact]
    public void OrdinaryLocalBundleCanStillPlanArchivesAndInf()
    {
        var sandbox = Directory.CreateTempSubdirectory("Omega-PcS-Boundary-");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(
                sandbox.FullName, "bundle"));
            File.WriteAllText(Path.Combine(folder.FullName, "driver.inf"),
                "[Version]\nSignature=\"$Windows NT$\"");
            File.WriteAllBytes(Path.Combine(folder.FullName, "gpu.zip"),
                [0x50, 0x4B, 0x03, 0x04]);

            var plan = PcSpecialistLocalCatalogPlan.Create(folder.FullName);

            Assert.Equal(folder.FullName, plan.RootPath);
            Assert.True(plan.HasLooseInf);
            Assert.Single(plan.Archives);
            Assert.Equal(2, plan.SourceCount);
            Assert.False(plan.IsInstallable);
            Assert.False(plan.IsVerifiedOfficialSource);
            Assert.False(plan.IsPartial);
        }
        finally
        {
            sandbox.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsParentJunctionEvenWhenSelectedFolderIsPlain(
        bool nested)
    {
        var sandbox = Directory.CreateTempSubdirectory("Omega-PcS-Boundary-");
        var link = Path.Combine(sandbox.FullName, "redirect");
        try
        {
            var outside = Directory.CreateDirectory(Path.Combine(
                sandbox.FullName, "outside", "driver-bundle", "inner"));
            File.WriteAllText(Path.Combine(outside.FullName, "gpu.inf"),
                "[Version]\nSignature=\"$Windows NT$\"");
            Directory.CreateSymbolicLink(link, Path.Combine(
                sandbox.FullName, "outside"));

            var target = nested
                ? Path.Combine(link, "driver-bundle", "inner")
                : Path.Combine(link, "driver-bundle");
            Assert.True(Directory.Exists(target));
            // The *target* leaf is not a reparse point, but its ancestor is.
            Assert.Equal(FileAttributes.Directory,
                File.GetAttributes(target) &
                (FileAttributes.Directory | FileAttributes.ReparsePoint));

            var error = Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(target));
            Assert.Contains("collegamento", error.Message);
            Assert.DoesNotContain(sandbox.FullName, error.Message);
            Assert.True(File.Exists(Path.Combine(outside.FullName, "gpu.inf")));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            sandbox.Delete(recursive: true);
        }
    }

    [Fact]
    public void DirectSymlinkRootIsNotAValidBundle()
    {
        var sandbox = Directory.CreateTempSubdirectory("Omega-PcS-Boundary-");
        var link = Path.Combine(sandbox.FullName, "redirect");
        try
        {
            var outside = Directory.CreateDirectory(Path.Combine(
                sandbox.FullName, "outside"));
            File.WriteAllText(Path.Combine(outside.FullName, "gpu.inf"),
                "[Version]");
            Directory.CreateSymbolicLink(link, outside.FullName);

            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(link));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            sandbox.Delete(recursive: true);
        }
    }

    [Fact]
    public void MissingRootAndUncAreRejectedBeforeAnyDirectoryEnumeration()
    {
        var sandbox = Directory.CreateTempSubdirectory("Omega-PcS-Boundary-");
        try
        {
            var missing = Path.Combine(sandbox.FullName, "PRIVATE_ORDER", "missing");
            var error = Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(missing));
            Assert.DoesNotContain("PRIVATE_ORDER", error.Message);

            var unc = Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(
                    @"\\PRIVATE_SERVER\PRIVATE_SHARE\driver-folder"));
            Assert.Contains("locale", unc.Message);
            Assert.DoesNotContain("PRIVATE_SERVER", unc.Message);
        }
        finally
        {
            sandbox.Delete(recursive: true);
        }
    }
}
