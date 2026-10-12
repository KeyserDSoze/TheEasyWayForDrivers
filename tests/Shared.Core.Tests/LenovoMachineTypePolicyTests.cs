using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LenovoMachineTypePolicyTests
{
    [Theory]
    [InlineData("21HD", "21HD")]
    [InlineData(" 21hd ", "21HD")]
    [InlineData("20T1", "20T1")]
    public void MachineType_AcceptsFourAlphanumericCharacters(string raw, string expected)
    {
        Assert.True(LenovoMachineTypePolicy.TryNormalize(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("21")]
    [InlineData("21HDA")]
    [InlineData("21HD;exit")]
    [InlineData("$env:PATH")]
    [InlineData("a b ")]
    public void MachineType_RejectsUnknownOrUnsafeInput(string raw) =>
        Assert.False(LenovoMachineTypePolicy.TryNormalize(raw, out _));

    [Fact]
    public void MachineType_RejectsNull() =>
        Assert.False(LenovoMachineTypePolicy.TryNormalize(null, out _));
}
