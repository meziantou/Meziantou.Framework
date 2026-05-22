using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting;

public sealed class Image : IEquatable<Image>
{
    private readonly uint[] _pixels;

    private Image(int width, int height, uint[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<uint> Pixels => _pixels;

    public static async Task<Image> LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = File.OpenRead(path);
        return await LoadAsync(stream).ConfigureAwait(false);
    }

    public static async Task<Image> LoadAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var data = await ReadAllBytesAsync(stream).ConfigureAwait(false);
        return Load(data);
    }

    internal static Image Load(ReadOnlySpan<byte> data)
    {
        return DetectFormat(data) switch
        {
            DetectedImageFormat.Bmp => LoadBmp(data),
            _ => throw new NotSupportedException("Unsupported image format. Only BMP is currently supported."),
        };
    }

    public bool Equals(Image? other)
    {
        if (other is null)
            return false;

        return Width == other.Width &&
               Height == other.Height &&
               _pixels.AsSpan().SequenceEqual(other._pixels);
    }

    public override bool Equals(object? obj) => obj is Image image && Equals(image);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Width);
        hash.Add(Height);
        foreach (var pixel in _pixels)
        {
            hash.Add(pixel);
        }

        return hash.ToHashCode();
    }

    private static DetectedImageFormat DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M')
            return DetectedImageFormat.Bmp;

        return DetectedImageFormat.Unknown;
    }

    private static Image LoadBmp(ReadOnlySpan<byte> data)
    {
        const int BmpFileHeaderSize = 14;
        const int BmpInfoHeaderSize = 40;

        if (data.Length < BmpFileHeaderSize + BmpInfoHeaderSize)
            throw new InvalidDataException("The BMP data is too small.");

        var dibHeaderSize = ReadInt32LittleEndian(data, 14);
        if (dibHeaderSize < BmpInfoHeaderSize)
            throw new NotSupportedException("Unsupported BMP DIB header.");

        if (data.Length < BmpFileHeaderSize + dibHeaderSize)
            throw new InvalidDataException("The BMP data is truncated.");

        var pixelDataOffset = checked((int)ReadUInt32LittleEndian(data, 10));
        var width = ReadInt32LittleEndian(data, 18);
        var height = ReadInt32LittleEndian(data, 22);
        var planes = ReadUInt16LittleEndian(data, 26);
        var bitsPerPixel = ReadUInt16LittleEndian(data, 28);
        var compression = ReadUInt32LittleEndian(data, 30);

        if (width <= 0)
            throw new NotSupportedException("Unsupported BMP width.");

        if (height == 0 || height == int.MinValue)
            throw new NotSupportedException("Unsupported BMP height.");

        if (planes != 1)
            throw new NotSupportedException("Unsupported BMP color planes.");

        if (compression != 0)
            throw new NotSupportedException("Only uncompressed BMP data is supported.");

        if (bitsPerPixel is not 24 and not 32)
            throw new NotSupportedException("Only 24-bit and 32-bit BMP data is supported.");

        if (pixelDataOffset < BmpFileHeaderSize + dibHeaderSize || pixelDataOffset >= data.Length)
            throw new InvalidDataException("The BMP pixel data offset is invalid.");

        var bytesPerPixel = bitsPerPixel / 8;
        var absoluteHeight = Math.Abs(height);
        var topDown = height < 0;
        var rowWithoutPadding = checked(width * bytesPerPixel);
        var rowStride = checked((rowWithoutPadding + 3) & ~3);
        var pixelDataSize = checked(rowStride * absoluteHeight);
        if (pixelDataOffset + pixelDataSize > data.Length)
            throw new InvalidDataException("The BMP pixel data is truncated.");

        var pixels = new uint[checked(width * absoluteHeight)];
        for (var y = 0; y < absoluteHeight; y++)
        {
            var sourceRow = topDown ? y : absoluteHeight - y - 1;
            var sourceOffset = pixelDataOffset + sourceRow * rowStride;
            var destinationOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = sourceOffset + x * bytesPerPixel;
                var b = data[pixelOffset];
                var g = data[pixelOffset + 1];
                var r = data[pixelOffset + 2];
                var a = bitsPerPixel == 32 ? data[pixelOffset + 3] : (byte)0xFF;
                pixels[destinationOffset + x] = (uint)(a << 24 | r << 16 | g << 8 | b);
            }
        }

        return new Image(width, absoluteHeight, pixels);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 4 > data.Length)
            throw new InvalidDataException("The image data is truncated.");

        return BinaryPrimitives.ReadInt32LittleEndian(data[offset..(offset + 4)]);
    }

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 4 > data.Length)
            throw new InvalidDataException("The image data is truncated.");

        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..(offset + 4)]);
    }

    private static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 2 > data.Length)
            throw new InvalidDataException("The image data is truncated.");

        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..(offset + 2)]);
    }

    private enum DetectedImageFormat
    {
        Unknown,
        Bmp,
    }
}
