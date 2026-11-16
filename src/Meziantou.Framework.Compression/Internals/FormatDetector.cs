using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals;

/// <summary>Detects the format of an archive from its first bytes, and decompresses the compressed tar archives.</summary>
internal static class FormatDetector
{
    // Enough for the signatures and for a tar header behind a compression header
    public const int PeekLength = 1024;

    private const int TarBlockSize = 512;

    /// <summary>Detects the format of the outer layer: an archive, a compressed stream, or nothing known.</summary>
    public static (ArchiveFormat Format, CompressionFormat Compression) DetectOuter(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("PK\u0003\u0004"u8) || header.StartsWith("PK\u0005\u0006"u8) || header.StartsWith("PK\u0007\u0008"u8))
            return (ArchiveFormat.Zip, CompressionFormat.None);

        if (header.StartsWith((ReadOnlySpan<byte>)[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]))
            return (ArchiveFormat.SevenZip, CompressionFormat.None);

        if (header.StartsWith("Rar!\u001A\u0007\u0000"u8) || header.StartsWith("Rar!\u001A\u0007\u0001\u0000"u8))
            return (ArchiveFormat.Rar, CompressionFormat.None);

        if (header.StartsWith((ReadOnlySpan<byte>)[0x1F, 0x8B]))
            return (ArchiveFormat.Unknown, CompressionFormat.GZip);

        if (header.Length >= 4 && header.StartsWith("BZh"u8) && header[3] is >= (byte)'1' and <= (byte)'9')
            return (ArchiveFormat.Unknown, CompressionFormat.BZip2);

        if (header.StartsWith((ReadOnlySpan<byte>)[0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]))
            return (ArchiveFormat.Unknown, CompressionFormat.Xz);

        if (header.StartsWith((ReadOnlySpan<byte>)[0x28, 0xB5, 0x2F, 0xFD]))
            return (ArchiveFormat.Unknown, CompressionFormat.Zstandard);

        if (header.StartsWith("LZIP"u8))
            return (ArchiveFormat.Unknown, CompressionFormat.LZip);

        if (header.StartsWith((ReadOnlySpan<byte>)[0x1F, 0x9D]))
            return (ArchiveFormat.Unknown, CompressionFormat.UnixCompress);

        if (IsTarHeader(header))
            return (ArchiveFormat.Tar, CompressionFormat.None);

        if (IsLzmaAloneHeader(header))
            return (ArchiveFormat.Unknown, CompressionFormat.Lzma);

        return (ArchiveFormat.Unknown, CompressionFormat.None);
    }

    /// <summary>Gets whether a block is a tar header: the ustar magic, or a valid checksum (V7 archives).</summary>
    public static bool IsTarHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < TarBlockSize)
            return false;

        var block = header[..TarBlockSize];
        if (block[257..].StartsWith("ustar"u8))
            return true;

        // The checksum is the sum of the bytes of the header, with the checksum field counted as spaces, written in octal
        var field = block.Slice(148, 8);
        long expected = 0;
        var digits = 0;
        foreach (var b in field)
        {
            if (b is >= (byte)'0' and <= (byte)'7')
            {
                expected = (expected * 8) + (b - '0');
                digits++;
            }
            else if (b is (byte)' ' or 0)
            {
                if (digits > 0)
                    break;
            }
            else
            {
                return false;
            }
        }

        if (digits is 0)
            return false;

        long sum = 0;
        for (var i = 0; i < TarBlockSize; i++)
        {
            sum += i is >= 148 and < 156 ? ' ' : block[i];
        }

        // An all-zero block (the end of an archive) has the sum of the spaces only
        return sum == expected && block[0] is not 0;
    }

    /// <summary>The heuristic of xz for the headerless LZMA alone format: properties, a dictionary of 2^n or 2^n + 2^(n-1) bytes, a plausible size.</summary>
    private static bool IsLzmaAloneHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 13 || header[0] >= 9 * 5 * 5)
            return false;

        var dictionarySize = BinaryPrimitives.ReadUInt32LittleEndian(header[1..]);
        if (dictionarySize != uint.MaxValue)
        {
            var d = dictionarySize - 1;
            d |= d >> 2;
            d |= d >> 3;
            d |= d >> 4;
            d |= d >> 8;
            d |= d >> 16;
            d++;
            if (d != dictionarySize)
                return false;
        }

        var size = BinaryPrimitives.ReadUInt64LittleEndian(header[5..]);
        return size == ulong.MaxValue || size < (1UL << 38);
    }

    public static Stream CreateDecompressionStream(CompressionFormat format, Stream stream, bool leaveOpen)
    {
        return format switch
        {
            CompressionFormat.GZip => new GZipStream(stream, CompressionMode.Decompress, leaveOpen),
            CompressionFormat.BZip2 => new BZip2Stream(stream, CompressionMode.Decompress, leaveOpen),
            CompressionFormat.Xz => new XzStream(stream, CompressionMode.Decompress, leaveOpen),
            CompressionFormat.Lzma => new LzmaStream(stream, CompressionMode.Decompress, leaveOpen),
            CompressionFormat.LZip => new LZipStream(stream, CompressionMode.Decompress, leaveOpen),
            CompressionFormat.UnixCompress => new UnixCompressStream(stream, leaveOpen),
#if NET11_0_OR_GREATER
            CompressionFormat.Zstandard => new ZstandardStream(stream, CompressionMode.Decompress, leaveOpen),
#else
            CompressionFormat.Zstandard => throw new NotSupportedException("Zstandard requires .NET 11 or later."),
#endif
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>Gets whether the decompressed data start with a tar header.</summary>
    public static bool IsCompressedTar(CompressionFormat compression, Stream seekableStream)
    {
        var position = seekableStream.Position;
        try
        {
            using var decompressed = CreateDecompressionStream(compression, seekableStream, leaveOpen: true);
            var block = new byte[TarBlockSize];
            var read = decompressed.ReadAtLeast(block, block.Length, throwOnEndOfStream: false);
            return IsTarHeader(block.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EndOfStreamException)
        {
            return false;
        }
        finally
        {
            seekableStream.Position = position;
        }
    }

    /// <summary>Searches a seekable stream for an archive behind an executable stub (self-extracting archives): the RAR signature in the first MiB, or the end of central directory of zip.</summary>
    public static ArchiveFormat DetectSelfExtracting(Stream stream)
    {
        var position = stream.Position;
        try
        {
            var buffer = new byte[(int)Math.Min(1024 * 1024, stream.Length - position)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (buffer.AsSpan(0, read).IndexOf("Rar!\u001A\u0007"u8) >= 0)
                return ArchiveFormat.Rar;

            if (stream.Length - position >= 22)
            {
                var tailLength = (int)Math.Min(65535 + 22, stream.Length - position);
                var tail = new byte[tailLength];
                stream.Position = stream.Length - tailLength;
                stream.ReadExactlyOrThrow(tail);
                if (tail.AsSpan().LastIndexOf("PK\u0005\u0006"u8) >= 0)
                    return ArchiveFormat.Zip;
            }

            return ArchiveFormat.Unknown;
        }
        finally
        {
            stream.Position = position;
        }
    }
}
