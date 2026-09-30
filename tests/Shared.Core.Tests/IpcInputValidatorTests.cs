using TheEasyWayForDrivers.Core.Ipc;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class IpcInputValidatorTests
{
    [Fact]
    public void ValidateUpdateIds_AcceptsGuidUpdateIdsAndRemovesDuplicates()
    {
        var id = Guid.NewGuid().ToString();

        var result = IpcInputValidator.ValidateUpdateIds([id, id]);

        Assert.Single(result);
        Assert.Equal(id, result[0]);
    }

    [Fact]
    public void ValidateUpdateIds_RejectsInvalidUpdateId()
    {
        Assert.Throws<ArgumentException>(
            () => IpcInputValidator.ValidateUpdateIds(["not-an-update-id"]));
    }

    [Fact]
    public void ValidateUpdateIds_RejectsOversizedRequest()
    {
        var ids = Enumerable
            .Range(0, IpcInputValidator.MaximumUpdateIds + 1)
            .Select(_ => Guid.NewGuid().ToString())
            .ToArray();

        Assert.Throws<ArgumentException>(
            () => IpcInputValidator.ValidateUpdateIds(ids));
    }
}
