using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework;

/// <summary>
/// Provides methods to render a QR code as a PNG image.
/// </summary>
public static class QRCodePngRenderer
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrChunkType = [73, 72, 68, 82];
    private static readonly byte[] IdatChunkType = [73, 68, 65, 84];
    private static readonly byte[] IendChunkType = [73, 69, 78, 68];
    private static readonly uint[] Crc32Table = InitializeCrc32Table();

    /// <summary>Renders the QR code as PNG bytes with default options.</summary>
    public static byte[] ToPng(this QRCode qrCode)
    {
        return ToPng(qrCode, new QRCodePngOptions());
    }

    /// <summary>Renders the QR code as PNG bytes with the specified options.</summary>
    public static byte[] ToPng(this QRCode qrCode, QRCodePngOptions options)
    {
        using var stream = new MemoryStream();
        WriteToPng(qrCode, stream, options);

        return stream.ToArray();
    }

    /// <summary>Writes the QR code as PNG to the specified stream with default options.</summary>
    public static void WriteToPng(this QRCode qrCode, Stream stream)
    {
        WriteToPng(qrCode, stream, new QRCodePngOptions());
    }

    /// <summary>Writes the QR code as PNG to the specified stream with the specified options.</summary>
    public static void WriteToPng(this QRCode qrCode, Stream stream, QRCodePngOptions options)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var width = GetTotalDimension(qrCode.Width, options.QuietZoneModules, options.ModuleSize);
        var height = GetTotalDimension(qrCode.Height, options.QuietZoneModules, options.ModuleSize);
        var imageData = CreateImageData(qrCode, width, height, options);
        var compressedImageData = CompressImageData(imageData);

        WritePng(stream, width, height, compressedImageData);
    }

    private static int GetTotalDimension(int size, int quietZoneModules, int moduleSize)
    {
        var value = ((long)size + (2L * quietZoneModules)) * moduleSize;
        if (value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleSize), "The output image dimensions are too large.");
        }

        return (int)value;
    }

    private static byte[] CreateImageData(QRCode qrCode, int width, int height, QRCodePngOptions options)
    {
        var stride = width + 1;
        var dataLength = (long)stride * height;
        if (dataLength > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException("options.ModuleSize", "The output image is too large.");
        }

        var result = new byte[(int)dataLength];

        for (var row = 0; row < height; row++)
        {
            var rowOffset = row * stride;
            var sourceRow = (row / options.ModuleSize) - options.QuietZoneModules;
            for (var col = 0; col < width; col++)
            {
                var sourceCol = (col / options.ModuleSize) - options.QuietZoneModules;
                var isDark = sourceRow >= 0
                    && sourceRow < qrCode.Height
                    && sourceCol >= 0
                    && sourceCol < qrCode.Width
                    && qrCode[sourceRow, sourceCol];
                var value = (isDark ^ options.InvertColors) ? (byte)0 : (byte)255;
                result[rowOffset + 1 + col] = value;
            }
        }

        return result;
    }

    private static byte[] CompressImageData(ReadOnlySpan<byte> imageData)
    {
        using var output = new MemoryStream();
        using (var compressionStream = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressionStream.Write(imageData);
        }

        return output.ToArray();
    }

    private static void WritePng(Stream stream, int width, int height, ReadOnlySpan<byte> compressedImageData)
    {
        stream.Write(PngSignature);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 8;
        ihdrData[9] = 0;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;

        WriteChunk(stream, IhdrChunkType, ihdrData);
        WriteChunk(stream, IdatChunkType, compressedImageData);
        WriteChunk(stream, IendChunkType, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, (uint)data.Length);
        stream.Write(uintBuffer);
        stream.Write(chunkType);
        stream.Write(data);

        var crc = ComputeCrc32(chunkType, data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        crc = UpdateCrc32(crc, chunkType);
        crc = UpdateCrc32(crc, data);

        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = Crc32Table[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] InitializeCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? (crc >> 1) : (0xEDB88320u ^ (crc >> 1));
            }

            table[index] = crc;
        }

        return table;
    }
}
