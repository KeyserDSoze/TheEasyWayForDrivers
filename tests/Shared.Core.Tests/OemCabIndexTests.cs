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

    private static MemoryStream CabinetWithReserve(
        string fileName = @"drivers\graphics.inf",
        int headerReserve = 20, int folderReserve = 3,
        ushort flags = 0x0004)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(fileName);
        var fileOffset = checked((uint)(40 + headerReserve + 8 + folderReserve));
        var dataOffset = checked(fileOffset + (uint)(16 + name.Length + 1));
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output,
            System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x4643534du); writer.Write(0u);
            writer.Write(0u); writer.Write(0u); writer.Write(fileOffset); writer.Write(0u);
            writer.Write((byte)3); writer.Write((byte)1);
            writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write(flags); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)headerReserve);
            writer.Write((byte)folderReserve);
            writer.Write((byte)2); // CFDATA reserve; index must not parse blocks
            writer.Write(new byte[headerReserve]);
            writer.Write(dataOffset); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(new byte[folderReserve]);
            writer.Write(8u); writer.Write(0u); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(name); writer.Write((byte)0);
            writer.Write(new byte[8]);
            var length = (uint)output.Length;
            output.Position = 8;
            writer.Write(length);
        }
        return new MemoryStream(output.ToArray());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 0)]
    [InlineData(20, 3)]
    [InlineData(32, 16)]
    [InlineData(0, 255)]
    public void MicrosoftReservedHeaderAndFolderBytesAreAcceptedAsOpaqueMetadata(
        int headerReserve, int folderReserve)
    {
        using var cab = CabinetWithReserve(
            headerReserve: headerReserve, folderReserve: folderReserve);
        var report = OemCabIndex.Inspect(cab);
        Assert.Equal("drivers/graphics.inf", Assert.Single(report.Entries).Path);
        Assert.False(report.IsAuthenticated);
        Assert.False(report.IsInstallable);
    }

    [Theory]
    [InlineData((ushort)0x0001)]
    [InlineData((ushort)0x0002)]
    [InlineData((ushort)0x0005)]
    [InlineData((ushort)0x0006)]
    [InlineData((ushort)0x0008)]
    public void MultipartAndUnknownCabinetFlagsRemainRejected(ushort flags)
    {
        using var cab = CabinetWithReserve(flags: flags);
        Assert.Throws<InvalidDataException>(() => OemCabIndex.Inspect(cab));
    }

    [Fact]
    public void ReservationCannotOverrunFileTableOrCabinet()
    {
        var valid = CabinetWithReserve().ToArray();

        // Decrease coffFiles into the reserved header body.
        var offsetOverlaps = valid.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            offsetOverlaps.AsSpan(16, 4), 50u);
        using var overlapped = new MemoryStream(offsetOverlaps);
        Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(overlapped));

        // Invalid declared total size: never silently ignore extra data.
        var wrongSize = valid.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            wrongSize.AsSpan(8, 4), (uint)wrongSize.Length + 1);
        using var mismatched = new MemoryStream(wrongSize);
        Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(mismatched));

        // A truncated CFHEADER extension is not a legacy CAB.
        var missingExtension = Cabinet().ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
            missingExtension.AsSpan(30, 2), 4);
        using var truncated = new MemoryStream(missingExtension);
        Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(truncated));

        // Data folder must not overlap the file table or reserved region.
        var wrongData = valid.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            wrongData.AsSpan(60, 4), 36u);
        using var overlappingData = new MemoryStream(wrongData);
        Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(overlappingData));
    }

    [Fact]
    public void ExcessiveReserveAndUnsafePathsAreStillRejected()
    {
        using var tooBig = CabinetWithReserve(headerReserve: 60001);
        Assert.Throws<InvalidDataException>(() => OemCabIndex.Inspect(tooBig));

        foreach (var name in new[]
        {
            @"..\driver.inf", @"\\server\share\driver.inf",
            @"C:\Windows\evil.inf", @"folder\..\driver.inf"
        })
        {
            using var cab = CabinetWithReserve(fileName: name);
            Assert.Throws<InvalidDataException>(() => OemCabIndex.Inspect(cab));
        }
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
