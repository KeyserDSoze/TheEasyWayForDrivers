using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsDriverCandidateMergeTests
{
    private static DriverUpdateInfo Candidate(
        string id, string title, bool advanced = false) =>
        new(id, title, null, "Display", "NVIDIA", null, null, false,
            "NVIDIA", "GeForce RTX", @"PCI\VEN_10DE&DEV_28A0", null,
            false, false, advanced);

    [Fact]
    public void DifferentSourcesCannotInflateUniqueUpdateCount()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("nvidia-1", "NVIDIA GPU"),
            Candidate("intel-1", "Intel WiFi")
        ]));
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("NVIDIA-1", "duplicate NVIDIA"),
            Candidate("amd-1", "AMD graphics")
        ]));
        Assert.Equal(3, updates.Count);
        Assert.Equal("NVIDIA GPU", updates["nvidia-1"].Title);
    }

    [Fact]
    public void RecommendedResultOverridesAdvancedResultWithoutExtraCount()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
            [Candidate("same", "Advanced", advanced: true)]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(updates,
            [Candidate("SAME", "Recommended")]));
        Assert.Equal("Recommended", updates["same"].Title);
        Assert.False(updates["same"].IsAdvancedCandidate);
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(updates,
            [Candidate("same", "New advanced", advanced: true)]));
        Assert.Equal("Recommended", updates["same"].Title);
    }

    [Fact]
    public void InvalidBlankIdsNeverCountAsNewUpdates()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("", "Fake"),
            Candidate(" ", "Fake whitespace"),
            Candidate("VALID", "Valid")
        ]));
        Assert.Single(updates);
    }

    [Fact]
    public void NewMicrosoftUpdateDriverMayIncreaseCountButNeverFabricatesCandidates()
    {
        var updates = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("wu-intel", "Intel WiFi"),
            Candidate("wu-bt", "Intel Bluetooth")
        ]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(updates, []));
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(updates,
        [
            Candidate("wu-intel", "Duplicate"),
            Candidate("mu-nvidia", "NVIDIA GeForce RTX")
        ]));
        Assert.Equal(3, updates.Count); // Synthetic fixture, NOT a hardware claim.
    }
}
