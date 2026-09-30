using System.Buffers.Binary;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PeSignatureInspectorTests
{
    [Fact]
    public void HasEmbeddedAuthenticodeSignature_DetectsSecurityDirectory()
    {
        using var stream =
            new MemoryStream(
                CreatePe(hasSignature: true));

        Assert.True(
            PeSignatureInspector
                .HasEmbeddedAuthenticodeSignature(
                    stream));
    }

    [Fact]
    public void HasEmbeddedAuthenticodeSignature_ReturnsFalseWithoutSecurityDirectory()
    {
        using var stream =
            new MemoryStream(
                CreatePe(hasSignature: false));

        Assert.False(
            PeSignatureInspector
                .HasEmbeddedAuthenticodeSignature(
                    stream));
    }

    [Fact]
    public void HasEmbeddedAuthenticodeSignature_ReturnsFalseForInvalidPe()
    {
        using var stream =
            new MemoryStream(new byte[128]);

        Assert.False(
            PeSignatureInspector
                .HasEmbeddedAuthenticodeSignature(
                    stream));
    }

    private static byte[] CreatePe(
        bool hasSignature)
    {
        var bytes =
            new byte[512];

        const int peOffset = 128;
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(0x3C, 4),
            peOffset);

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(peOffset, 4),
            0x00004550);

        const int optionalHeaderOffset =
            peOffset + 24;

        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(optionalHeaderOffset, 2),
            0x20B);

        const int securityDirectoryOffset =
            optionalHeaderOffset + 112 + 4 * 8;

        if (hasSignature)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(
                    securityDirectoryOffset,
                    4),
                400);

            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(
                    securityDirectoryOffset + 4,
                    4),
                32);
        }

        return bytes;
    }
}
