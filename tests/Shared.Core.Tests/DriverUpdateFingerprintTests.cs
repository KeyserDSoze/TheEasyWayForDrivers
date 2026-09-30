using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverUpdateFingerprintTests
{
    [Fact]
    public void Create_IsOrderIndependent()
    {
        var first = CreateUpdate("b");
        var second = CreateUpdate("a");

        Assert.Equal(
            DriverUpdateFingerprint.Create(
                [first, second]),
            DriverUpdateFingerprint.Create(
                [second, first]));
    }

    [Fact]
    public void Create_DeduplicatesIdsIgnoringCase()
    {
        Assert.Equal(
            "abc",
            DriverUpdateFingerprint.Create(
                [
                    CreateUpdate("abc"),
                    CreateUpdate("ABC")
                ]));
    }

    [Fact]
    public void Create_ChangesWhenUpdateSetChanges()
    {
        var first =
            DriverUpdateFingerprint.Create(
                [CreateUpdate("a")]);

        var second =
            DriverUpdateFingerprint.Create(
                [
                    CreateUpdate("a"),
                    CreateUpdate("b")
                ]);

        Assert.NotEqual(
            first,
            second);
    }

    private static DriverUpdateInfo CreateUpdate(
        string id) =>
        new(
            id,
            "Update",
            null,
            null,
            null,
            null,
            0,
            false,
            null,
            null,
            null,
            null);
}
