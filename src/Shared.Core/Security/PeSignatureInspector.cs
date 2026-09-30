using System.Buffers.Binary;

namespace TheEasyWayForDrivers.Core.Security;

public static class PeSignatureInspector
{
    private const uint PeSignature = 0x00004550;
    private const ushort Pe32Magic = 0x10B;
    private const ushort Pe32PlusMagic = 0x20B;
    private const int SecurityDirectoryIndex = 4;

    public static bool HasEmbeddedAuthenticodeSignature(
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.Open(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        return HasEmbeddedAuthenticodeSignature(stream);
    }

    public static bool HasEmbeddedAuthenticodeSignature(
        Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The PE stream must be readable and seekable.",
                nameof(stream));
        }

        var originalPosition = stream.Position;

        try
        {
            if (stream.Length < 64)
            {
                return false;
            }

            Span<byte> buffer = stackalloc byte[4];

            stream.Position = 0x3C;
            if (stream.Read(buffer) != buffer.Length)
            {
                return false;
            }

            var peOffset =
                BinaryPrimitives.ReadInt32LittleEndian(buffer);

            if (peOffset < 0 ||
                peOffset > stream.Length - 28)
            {
                return false;
            }

            stream.Position = peOffset;
            if (stream.Read(buffer) != buffer.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(buffer) !=
                PeSignature)
            {
                return false;
            }

            var optionalHeaderOffset =
                peOffset + 24L;

            stream.Position = optionalHeaderOffset;
            Span<byte> magicBuffer = stackalloc byte[2];

            if (stream.Read(magicBuffer) != magicBuffer.Length)
            {
                return false;
            }

            var magic =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    magicBuffer);

            var dataDirectoryOffset =
                magic switch
                {
                    Pe32Magic => 96,
                    Pe32PlusMagic => 112,
                    _ => -1
                };

            if (dataDirectoryOffset < 0)
            {
                return false;
            }

            var securityDirectoryOffset =
                optionalHeaderOffset +
                dataDirectoryOffset +
                SecurityDirectoryIndex * 8L;

            if (securityDirectoryOffset >
                stream.Length - 8)
            {
                return false;
            }

            stream.Position =
                securityDirectoryOffset;

            Span<byte> directory = stackalloc byte[8];

            if (stream.Read(directory) != directory.Length)
            {
                return false;
            }

            var certificateOffset =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    directory[..4]);

            var certificateSize =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    directory[4..]);

            if (certificateOffset == 0 ||
                certificateSize == 0)
            {
                return false;
            }

            var certificateEnd =
                (ulong)certificateOffset +
                certificateSize;

            return certificateEnd <=
                   (ulong)stream.Length;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
