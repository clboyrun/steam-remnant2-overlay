using System.Buffers.Binary;
using System.IO.Compression;

namespace Remnant2Overlay.SaveReader;

/// <summary>
/// Unwraps the compressed container used by Remnant 2 .sav files.
/// </summary>
/// <remarks>
/// Layout: a 12-byte header (CRC32, uncompressed size, save version), then chunks.
/// Each chunk has a 49-byte header followed by zlib data:
/// tag (8), max chunk size (8), compression type (1), compressed size (8),
/// uncompressed size (8), then the two sizes repeated (16).
/// </remarks>
public static class SaveFile
{
    public const int FileHeaderSize = 12;
    public const int ChunkHeaderSize = 49;
    public const ulong ChunkTag = 0x222222229E2A83C1;

    public static byte[] Decompress(string path) => Decompress(File.ReadAllBytes(path));

    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < FileHeaderSize + ChunkHeaderSize)
            throw new InvalidDataException("File is too small to be a Remnant 2 save.");

        using var output = new MemoryStream();
        int offset = FileHeaderSize;
        while (offset < data.Length)
        {
            var header = data.Slice(offset, ChunkHeaderSize);
            if (BinaryPrimitives.ReadUInt64LittleEndian(header) != ChunkTag)
                throw new InvalidDataException($"Missing chunk tag at offset {offset}.");

            long compressedSize = BinaryPrimitives.ReadInt64LittleEndian(header[17..]);
            int start = offset + ChunkHeaderSize;
            if (compressedSize <= 0 || start + compressedSize > data.Length)
                throw new InvalidDataException($"Bad chunk size at offset {offset}.");

            using var input = new MemoryStream(data.Slice(start, (int)compressedSize).ToArray());
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            zlib.CopyTo(output);

            offset = start + (int)compressedSize;
        }

        return output.ToArray();
    }
}
