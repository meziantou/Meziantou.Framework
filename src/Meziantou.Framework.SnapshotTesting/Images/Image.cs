using System.Runtime.InteropServices;

namespace Meziantou.Framework.SnapshotTesting;

internal sealed class Image : IEquatable<Image>
{
    private readonly Argb[] _pixels;

    private Image(int width, int height, Argb[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<Argb> Pixels => _pixels;

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
        if (BmpImageLoader.IsBmp(data))
            return BmpImageLoader.Load(data);

        if (PngImageLoader.IsPng(data))
            return PngImageLoader.Load(data);

        throw new NotSupportedException("Unsupported image format. Only BMP and PNG are currently supported.");
    }

    internal static Image Create(int width, int height, Argb[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        return new Image(width, height, pixels);
    }

    public bool Equals(Image? other)
    {
        if (other is null)
            return false;

        if (Width != other.Width || Height != other.Height)
            return false;

        var expectedPixels = MemoryMarshal.Cast<Argb, uint>(_pixels.AsSpan());
        var actualPixels = MemoryMarshal.Cast<Argb, uint>(other._pixels.AsSpan());
        return expectedPixels.SequenceEqual(actualPixels);
    }

    public override bool Equals(object? obj) => obj is Image image && Equals(image);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Width);
        hash.Add(Height);
        var hashPixelCount = Math.Min(_pixels.Length, 32);
        for (var i = 0; i < hashPixelCount; i++)
        {
            hash.Add(_pixels[i].PackedValue);
        }

        return hash.ToHashCode();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
        return memoryStream.ToArray();
    }
}
