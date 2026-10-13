using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemCabIndexTests
{
    private static MemoryStream Cabinet(string fileName = "drivers\\network.inf", ushort flags = 0)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x4643534du); writer.Write(0u);
            writer.Write(0u); writer.Write(0u); writer.Write(44u); writer.Write(0u);
            writer.Write((byte)3); writer.Write((byte)1);
            writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write(flags); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(80u); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(8u); writer.Write(0u); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(System.Text.Encoding.UTF8.GetBytes(fileName)); writer.Write((byte)0);
            writer.Write(new byte[8]);
            var length = (uint)output.Length;
            output.Position = 8;
            writer.Write(length);
        }
        return new MemoryStream(output.ToArray());
    }

    [Fact]
    public void ListsMetadataWithoutVerifyingOrExtracting()
    {
        using var cab = Cabinet();
        var report = OemCabIndex.Inspect(cab);
        Assert.Equal("drivers/network.inf", Assert.Single(report.Entries).Path);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsAuthenticated);
    }

    [Theory]
    [InlineData("..\\evil.inf")]
    [InlineData("C:\\escape.sys")]
    [InlineData("sub\\..\\bad.cat")]
    public void UnsafePathsAreRejected(string name)
    {
        using var cab = Cabinet(name);
        Assert.Throws<InvalidDataException>(() => OemCabIndex.Inspect(cab));
    }

    [Fact]
    public void MultipartCabIsRejected()
    {
        using var cab = Cabinet(flags: 1);
        Assert.Throws<InvalidDataException>(() => OemCabIndex.Inspect(cab));
    }
}
