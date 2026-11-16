using System.Buffers.Binary;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #524: CFFILE headers and zero-terminated names belong entirely to the
/// cabinet directory, never to the first compressed CFDATA block.
/// Synthetic headers: no decompression or driver installation is performed.
/// </summary>
public sealed class OemCabFileTableBoundaryTests
{
    private static (byte[] Bytes, int DataOffset, int FolderTable) Cabinet(
        ushort folders = 1, params string[] fileNames)
    {
        var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8,
                   leaveOpen: true))
        {
            var fileOffset = 36u + 8u * folders;
            writer.Write(0x4643534du); // MSCF
            writer.Write(0u);          // reserved1
            writer.Write(0u);          // cabinet size placeholder
            writer.Write(0u);          // reserved2
            writer.Write(fileOffset);
            writer.Write(0u);          // reserved3
            writer.Write((byte)3);
            writer.Write((byte)1);
            writer.Write(folders);
            writer.Write((ushort)fileNames.Length);
            writer.Write((ushort)0);   // flags
            writer.Write((ushort)0);   // setID
            writer.Write((ushort)0);   // iCabinet

            const int folderTableOffset = 36;
            for (var i = 0; i < folders; i++)
            {
                writer.Write(0u);        // CFDATA offset, patched below
                writer.Write((ushort)0); // no CFDATA blocks: directory only
                writer.Write((ushort)0); // no compression
            }

            for (var i = 0; i < fileNames.Length; i++)
            {
                writer.Write(8u);         // declared uncompressed length
                writer.Write(0u);         // uncompressed folder offset
                writer.Write((ushort)(i % folders));
                writer.Write((ushort)0);  // date
                writer.Write((ushort)0);  // time
                writer.Write((ushort)0);  // attributes
                writer.Write(Encoding.UTF8.GetBytes(fileNames[i]));
                writer.Write((byte)0);
            }

            var firstData = checked((int)output.Position);
            writer.Write(new byte[16]); // synthetic CFDATA boundary/padding
            var total = checked((uint)output.Position);

            output.Position = 8;
            writer.Write(total);
            for (var i = 0; i < folders; i++)
            {
                output.Position = folderTableOffset + (8 * i);
                writer.Write((uint)firstData);
            }

            return (output.ToArray(), firstData, folderTableOffset);
        }
    }

    private static MemoryStream CorruptDataStart(
        (byte[] Bytes, int DataOffset, int FolderTable) baseCabinet,
        int relativeOffset, int folder = 0)
    {
        var bytes = baseCabinet.Bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(baseCabinet.FolderTable + (folder * 8), 4),
            checked((uint)(baseCabinet.DataOffset + relativeOffset)));
        return new MemoryStream(bytes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-8)]
    [InlineData(-15)]
    public void DataBeginningInsideCffileNameIsRejected(int delta)
    {
        var good = Cabinet(1, "drivers/graphics.inf");
        using var cab = CorruptDataStart(good, delta);

        var error = Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(cab));

        Assert.DoesNotContain("graphics", error.Message);
        Assert.DoesNotContain("drivers", error.Message);
        Assert.Contains("CFFILE", error.Message);
    }

    [Fact]
    public void CffileFixedHeaderCannotOverlapCfdData()
    {
        var good = Cabinet(1, "drivers/graphics.inf");
        // CFFILE starts at offset 44; its 16-byte fixed record must
        // finish before the first CFDATA byte, even if a terminator
        // happens to exist later inside the compressed data.
        using var cab = CorruptDataStart(good,
            relativeOffset: -(good.DataOffset - 54));
        var error = Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(cab));
        Assert.Contains("Tabella CFFILE", error.Message);
        Assert.DoesNotContain("graphics", error.Message);
    }

    [Fact]
    public void MissingNameTerminatorCannotBeBorrowedFromCompressedBytes()
    {
        var good = Cabinet(1, "gpu.inf");
        // The filename terminator is the byte immediately preceding the
        // CFDATA region. Replace it with 'A': the old index continued into
        // CFDATA to find a zero byte and accepted a phantom "gpu.infA".
        var corrupted = good.Bytes.ToArray();
        corrupted[good.DataOffset - 1] = (byte)'A';
        using var stream = new MemoryStream(corrupted);

        var error = Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(stream));
        Assert.Contains("non terminato", error.Message);
        Assert.DoesNotContain("gpu", error.Message);
    }

    [Fact]
    public void EarliestCfdDataFromSecondFolderBoundsEntireFileTable()
    {
        var good = Cabinet(2, "audio/realtek.inf");
        using var valid = new MemoryStream(good.Bytes);
        Assert.Equal("audio/realtek.inf",
            Assert.Single(OemCabIndex.Inspect(valid).Entries).Path);

        // The first folder's data offset is valid, but the second
        // folder starts inside the CFFILE filename. The minimum offset
        // across ALL CFFOLDERs must decide the preflight limit.
        using var corrupt = CorruptDataStart(good, -1, folder: 1);
        Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(corrupt));
    }

    [Fact]
    public void ValidCffileTerminatorExactlyAtDataBoundaryIsAccepted()
    {
        var good = Cabinet(1, "nvidia/graphics.inf", "killer/wifi.inf");
        using var cabinet = new MemoryStream(good.Bytes);
        var result = OemCabIndex.Inspect(cabinet);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("nvidia/graphics.inf", result.Entries[0].Path);
        Assert.Equal("killer/wifi.inf", result.Entries[1].Path);
        Assert.False(result.IsAuthenticated);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void SecondCffileMustNotBeginInsideCompressedBytes()
    {
        var good = Cabinet(1, "nvidia.inf", "killer.inf");
        // The second CFFILE starts 16+11 bytes after the first. Move
        // the first data offset into the second fixed record.
        var firstNameBytes = Encoding.UTF8.GetByteCount("nvidia.inf") + 1;
        var secondHeaderStart = 44 + 16 + firstNameBytes;
        var fakeDataOffset = secondHeaderStart + 6;
        using var stream = CorruptDataStart(good,
            fakeDataOffset - good.DataOffset);

        Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(stream));
    }
}
