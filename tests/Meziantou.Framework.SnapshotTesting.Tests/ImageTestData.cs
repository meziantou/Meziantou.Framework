using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting.Tests;

internal static class ImageTestData
{
    private static readonly string ResourcePrefix = typeof(ImageTestData).Namespace + ".TestAssets.images.";
    private static readonly Assembly Assembly = typeof(ImageTestData).Assembly;

    public static byte[] ReadImageFixture(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var stream = OpenImageFixtureStream(fileName);
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    public static async Task<Image> LoadImageFixtureAsync(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        await using var stream = OpenImageFixtureStream(fileName);
        return await Image.LoadAsync(stream).ConfigureAwait(false);
    }

    private static Stream OpenImageFixtureStream(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded test fixture '{fileName}' was not found.");

        return stream;
    }

    public static byte[] CreateJpegProgressive()
    {
        return Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wgARCAABAAIDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUAQEAAAAAAAAAAAAAAAAAAAAI/9oADAMBAAIQAxAAAAGyC4Rv/8QAFRABAQAAAAAAAAAAAAAAAAAABTX/2gAIAQEAAQUCGj//xAAbEQAABwEAAAAAAAAAAAAAAAAAAQMEBjZzsv/aAAgBAwEBPwGUWZ9sp2Y//8QAGREAAQUAAAAAAAAAAAAAAAAAAAECAzNx/9oACAECAQE/AZ7nap//xAAYEAACAwAAAAAAAAAAAAAAAAAAAgR0sv/aAAgBAQAGPwKJWTJ//8QAFhAAAwAAAAAAAAAAAAAAAAAAAFHw/9oACAEBAAE/IaKH/9oADAMBAAIAAwAAABDz/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAwEBPxBF/8QAFxEAAwEAAAAAAAAAAAAAAAAAAAFR8P/aAAgBAgEBPxDMrP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8Qbf/Z");
    }

    public static byte[] CreateJpegCmyk()
    {
        return Convert.FromBase64String("/9j/7gAOQWRvYmUAZAAAAAAA/9sAQwACAQEBAQECAQEBAgICAgIEAwICAgIFBAQDBAYFBgYGBQYGBgcJCAYHCQcGBggLCAkKCgoKCgYICwwLCgwJCgoK/8AAFAgAAQABBEMRAE0RAFkRAEsRAP/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/aAA4EQwBNAFkASwAAPwD9/K/n/r+f+v38r//Z");
    }

    public static byte[] AddJpegCommentSegment(byte[] jpegData, string comment)
    {
        ArgumentNullException.ThrowIfNull(jpegData);
        ArgumentNullException.ThrowIfNull(comment);

        if (jpegData.Length < 2 || jpegData[0] != 0xFF || jpegData[1] != 0xD8)
            throw new ArgumentException("Expected JPEG data", nameof(jpegData));

        var commentData = Encoding.ASCII.GetBytes(comment);
        var segmentLength = checked(commentData.Length + 2);
        if (segmentLength > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(comment));

        var result = new byte[checked(jpegData.Length + 4 + commentData.Length)];
        result[0] = 0xFF;
        result[1] = 0xD8;
        result[2] = 0xFF;
        result[3] = 0xFE;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4, 2), (ushort)segmentLength);
        commentData.CopyTo(result.AsSpan(6, commentData.Length));
        jpegData.AsSpan(2).CopyTo(result.AsSpan(6 + commentData.Length));
        return result;
    }

    public static byte[] CreateBmp24(int width, int height, IReadOnlyList<uint> pixels, int pixelsPerMeter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Count != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        const int FileHeaderSize = 14;
        const int InfoHeaderSize = 40;
        var rowSizeWithoutPadding = checked(width * 3);
        var rowStride = (rowSizeWithoutPadding + 3) & ~3;
        var pixelDataSize = checked(rowStride * height);
        var data = new byte[FileHeaderSize + InfoHeaderSize + pixelDataSize];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        WriteUInt32LittleEndian(data, 2, (uint)data.Length);
        WriteUInt32LittleEndian(data, 10, FileHeaderSize + InfoHeaderSize);
        WriteUInt32LittleEndian(data, 14, InfoHeaderSize);
        WriteInt32LittleEndian(data, 18, width);
        WriteInt32LittleEndian(data, 22, height);
        WriteUInt16LittleEndian(data, 26, 1);
        WriteUInt16LittleEndian(data, 28, 24);
        WriteUInt32LittleEndian(data, 30, 0);
        WriteUInt32LittleEndian(data, 34, (uint)pixelDataSize);
        WriteInt32LittleEndian(data, 38, pixelsPerMeter);
        WriteInt32LittleEndian(data, 42, pixelsPerMeter);

        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - y - 1;
            var sourceOffset = sourceRow * width;
            var destinationOffset = FileHeaderSize + InfoHeaderSize + y * rowStride;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[sourceOffset + x];
                data[destinationOffset + x * 3] = (byte)(pixel & 0xFF);
                data[destinationOffset + x * 3 + 1] = (byte)((pixel >> 8) & 0xFF);
                data[destinationOffset + x * 3 + 2] = (byte)((pixel >> 16) & 0xFF);
            }
        }

        return data;
    }

    public static byte[] CreatePngRgba32(int width, int height, IReadOnlyList<uint> pixels, float? gamma = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Count != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        var rowStride = checked(width * 4 + 1);
        var imageData = new byte[checked(rowStride * height)];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowStride;
            imageData[rowOffset] = 0;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                var pixelOffset = rowOffset + 1 + x * 4;
                imageData[pixelOffset] = (byte)((pixel >> 16) & 0xFF);
                imageData[pixelOffset + 1] = (byte)((pixel >> 8) & 0xFF);
                imageData[pixelOffset + 2] = (byte)(pixel & 0xFF);
                imageData[pixelOffset + 3] = (byte)(pixel >> 24);
            }
        }

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(imageData);
            }

            compressedData = compressedStream.ToArray();
        }

        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 8;
        ihdrData[9] = 6;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(stream, "IHDR"u8, ihdrData);

        if (gamma is not null)
        {
            Span<byte> gammaData = stackalloc byte[4];
            var gammaValue = checked((uint)Math.Round(gamma.Value * 100000f, MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteUInt32BigEndian(gammaData, gammaValue);
            WritePngChunk(stream, "gAMA"u8, gammaData);
        }

        WritePngChunk(stream, "IDAT"u8, compressedData);
        WritePngChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    /// <summary>Builds an uncompressed, non-interlaced PNG with an arbitrary color type and bit depth.</summary>
    /// <param name="samples">The raw scanlines, without the per-row filter byte.</param>
    public static byte[] CreatePng(int width, int height, byte bitDepth, byte colorType, byte[] samples, byte[]? palette = null, byte[]? transparency = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(samples);

        var channelCount = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType)),
        };

        var rowLength = (width * channelCount * bitDepth + 7) / 8;
        if (samples.Length != checked(rowLength * height))
            throw new ArgumentOutOfRangeException(nameof(samples));

        var imageData = new byte[checked((rowLength + 1) * height)];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * (rowLength + 1);
            imageData[rowOffset] = 0;
            samples.AsSpan(y * rowLength, rowLength).CopyTo(imageData.AsSpan(rowOffset + 1));
        }

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(imageData);
            }

            compressedData = compressedStream.ToArray();
        }

        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = bitDepth;
        ihdrData[9] = colorType;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(stream, "IHDR"u8, ihdrData);

        if (palette is not null)
        {
            WritePngChunk(stream, "PLTE"u8, palette);
        }

        if (transparency is not null)
        {
            WritePngChunk(stream, "tRNS"u8, transparency);
        }

        WritePngChunk(stream, "IDAT"u8, compressedData);
        WritePngChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes a PNG whose IDAT payload is supplied verbatim, so the inflated size can deliberately disagree
    /// with what the IHDR dimensions imply.
    /// </summary>
    public static byte[] CreatePngWithRawImageData(int width, int height, byte bitDepth, byte colorType, byte[] rawImageData)
    {
        ArgumentNullException.ThrowIfNull(rawImageData);

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(rawImageData);
            }

            compressedData = compressedStream.ToArray();
        }

        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = bitDepth;
        ihdrData[9] = colorType;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(stream, "IHDR"u8, ihdrData);
        WritePngChunk(stream, "IDAT"u8, compressedData);
        WritePngChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    public static byte[] CreateIcoWithPngEntries(params byte[][] pngImages)
    {
        ArgumentNullException.ThrowIfNull(pngImages);
        if (pngImages.Length == 0)
            throw new ArgumentException("At least one PNG image is required.", nameof(pngImages));

        var directorySize = checked(6 + pngImages.Length * 16);
        var imageDataSize = pngImages.Sum(static image => image.Length);
        var result = new byte[checked(directorySize + imageDataSize)];

        WriteUInt16LittleEndian(result, 0, 0);
        WriteUInt16LittleEndian(result, 2, 1);
        WriteUInt16LittleEndian(result, 4, checked((ushort)pngImages.Length));

        var imageOffset = directorySize;
        for (var i = 0; i < pngImages.Length; i++)
        {
            var image = pngImages[i];
            if (image is null)
                throw new ArgumentException("PNG image cannot be null.", nameof(pngImages));

            if (image.Length == 0)
                throw new ArgumentException("PNG image cannot be empty.", nameof(pngImages));

            var entryOffset = 6 + i * 16;
            result[entryOffset] = 1; // Width
            result[entryOffset + 1] = 1; // Height
            result[entryOffset + 2] = 0; // Color count
            result[entryOffset + 3] = 0; // Reserved
            WriteUInt16LittleEndian(result, entryOffset + 4, 1); // Planes
            WriteUInt16LittleEndian(result, entryOffset + 6, 32); // Bit count
            WriteUInt32LittleEndian(result, entryOffset + 8, checked((uint)image.Length));
            WriteUInt32LittleEndian(result, entryOffset + 12, checked((uint)imageOffset));

            image.CopyTo(result, imageOffset);
            imageOffset += image.Length;
        }

        return result;
    }

    public static byte[] CreateGif(int width, int height, IReadOnlyList<uint> colorTable, byte backgroundColorIndex, params GifFrameData[] frames)
    {
        ArgumentNullException.ThrowIfNull(colorTable);
        ArgumentNullException.ThrowIfNull(frames);
        if (colorTable.Count == 0)
            throw new ArgumentException("At least one color is required.", nameof(colorTable));

        if (frames.Length == 0)
            throw new ArgumentException("At least one frame is required.", nameof(frames));

        var colorBits = 1;
        while ((1 << colorBits) < colorTable.Count)
        {
            colorBits++;
        }

        using var stream = new MemoryStream();
        stream.Write("GIF89a"u8);
        WriteGifUInt16(stream, width);
        WriteGifUInt16(stream, height);
        stream.WriteByte((byte)(0b1000_0000 | (colorBits - 1))); // Global color table of 2^colorBits entries
        stream.WriteByte(backgroundColorIndex);
        stream.WriteByte(0); // Pixel aspect ratio

        var colorCount = 1 << colorBits;
        for (var i = 0; i < colorCount; i++)
        {
            var color = i < colorTable.Count ? colorTable[i] : 0u;
            stream.WriteByte((byte)((color >> 16) & 0xFF));
            stream.WriteByte((byte)((color >> 8) & 0xFF));
            stream.WriteByte((byte)(color & 0xFF));
        }

        var minimumCodeSize = Math.Max(2, colorBits);
        foreach (var frame in frames)
        {
            if (frame.PixelIndexes.Count != checked(frame.Width * frame.Height))
                throw new ArgumentException("The number of pixels does not match the frame size.", nameof(frames));

            var hasTransparency = frame.TransparentColorIndex >= 0;
            stream.WriteByte(0x21); // Extension introducer
            stream.WriteByte(0xF9); // Graphic control extension
            stream.WriteByte(4); // Block size
            stream.WriteByte((byte)((frame.DisposalMethod << 2) | (hasTransparency ? 1 : 0)));
            WriteGifUInt16(stream, 0); // Delay
            stream.WriteByte(hasTransparency ? checked((byte)frame.TransparentColorIndex) : (byte)0);
            stream.WriteByte(0); // Block terminator

            stream.WriteByte(0x2C); // Image separator
            WriteGifUInt16(stream, frame.Left);
            WriteGifUInt16(stream, frame.Top);
            WriteGifUInt16(stream, frame.Width);
            WriteGifUInt16(stream, frame.Height);
            stream.WriteByte(0); // No local color table, not interlaced

            stream.WriteByte((byte)minimumCodeSize);
            WriteGifSubBlocks(stream, EncodeGifPixels(frame.PixelIndexes, minimumCodeSize));
        }

        stream.WriteByte(0x3B); // Trailer
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes the pixels without ever growing the LZW dictionary: every pixel is preceded by a clear code, so
    /// all codes keep the same size. The output is larger than a real encoder would produce but is valid.
    /// </summary>
    private static byte[] EncodeGifPixels(IReadOnlyList<byte> pixelIndexes, int minimumCodeSize)
    {
        var clearCode = 1 << minimumCodeSize;
        var endCode = clearCode + 1;
        var codeSize = minimumCodeSize + 1;

        using var stream = new MemoryStream();
        var bitBuffer = 0u;
        var bitCount = 0;

        foreach (var pixelIndex in pixelIndexes)
        {
            WriteCode(clearCode);
            WriteCode(pixelIndex);
        }

        WriteCode(endCode);
        while (bitCount > 0)
        {
            stream.WriteByte((byte)(bitBuffer & 0xFF));
            bitBuffer >>= 8;
            bitCount -= Math.Min(bitCount, 8);
        }

        return stream.ToArray();

        void WriteCode(int code)
        {
            bitBuffer |= (uint)code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                stream.WriteByte((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }
    }

    private static void WriteGifSubBlocks(Stream stream, ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var blockSize = Math.Min(255, data.Length);
            stream.WriteByte((byte)blockSize);
            stream.Write(data[..blockSize]);
            data = data[blockSize..];
        }

        stream.WriteByte(0); // Block terminator
    }

    private static void WriteGifUInt16(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, checked((ushort)value));
        stream.Write(buffer);
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, (uint)data.Length);
        stream.Write(uintBuffer);
        stream.Write(type);
        stream.Write(data);

        var crc = ComputePngCrc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        crc = UpdatePngCrc32(crc, type);
        crc = UpdatePngCrc32(crc, data);
        return ~crc;
    }

    private static uint UpdatePngCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
            }
        }

        return crc;
    }

    private static void WriteUInt32LittleEndian(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private static void WriteInt32LittleEndian(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private static void WriteUInt16LittleEndian(byte[] data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);
    }

    internal sealed record GifFrameData(int Left, int Top, int Width, int Height, IReadOnlyList<byte> PixelIndexes)
    {
        public int DisposalMethod { get; init; }
        public int TransparentColorIndex { get; init; } = -1;
    }
}
