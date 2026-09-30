using TheEasyWayForDrivers.Core.Formatting;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(null, "N/D")]
    [InlineData(0L, "N/D")]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.00 GB")]
    public void Format_UsesAppropriateUnit(
        long? size,
        string expected)
    {
        Assert.Equal(
            expected,
            ByteSizeFormatter.Format(size));
    }
}
