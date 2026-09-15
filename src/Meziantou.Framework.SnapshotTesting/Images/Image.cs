using System.Runtime.InteropServices;

namespace Meziantou.Framework.SnapshotTesting;

internal sealed class Image : IEquatable<Image>
{
    private readonly Argb[] _pixels;
    private readonly ushort[]? _highPrecisionSamples;

    private Image(int width, int height, Argb[] pixels, ushort[]? highPrecisionSamples)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
        _highPrecisionSamples = highPrecisionSamples;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<Argb> Pixels => _pixels;

    /// <summary>
    /// Gets the A, R, G and B 16-bit samples of every pixel when the source stores more than 8 bits per sample
    /// (16-bit PNG), otherwise an empty buffer. <see cref="Pixels"/> holds the same values reduced to 8 bits.
    /// </summary>
    public ReadOnlyMemory<ushort> HighPrecisionSamples => _highPrecisionSamples;

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

    /// <summary>
    /// Decodes an image. Callers treat a decode failure as "these two snapshots differ", so every way a
    /// malformed file can fail has to arrive as <see cref="InvalidDataException" /> or
    /// <see cref="NotSupportedException" />. The decoders narrow file-supplied 32-bit values with checked
    /// casts and index into the data with offsets derived from them, so an arithmetic or range failure is a
    /// statement about the file rather than a bug. Likewise, the decoders size their buffers from the header, so
    /// a header announcing dimensions no array can hold surfaces as an <see cref="OutOfMemoryException" />.
    /// </summary>
    internal static Image Load(ReadOnlySpan<byte> data)
    {
        try
        {
            return LoadCore(data);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("The image data contains an out-of-range value.", ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new InvalidDataException("The image data is truncated or inconsistent.", ex);
        }
        catch (ArgumentException ex)
        {
            // Includes ArgumentOutOfRangeException, thrown when slicing past the end of the data
            throw new InvalidDataException("The image data is truncated or inconsistent.", ex);
        }
        catch (OutOfMemoryException ex)
        {
            throw new InvalidDataException("The image dimensions are too large to decode.", ex);
        }
    }

    private static Image LoadCore(ReadOnlySpan<byte> data)
    {
        if (BmpImageLoader.IsBmp(data))
            return BmpImageLoader.Load(data);

        if (PngImageLoader.IsPng(data))
            return PngImageLoader.Load(data);

        if (JpegImageLoader.IsJpeg(data))
            return JpegImageLoader.Load(data);

        if (TiffImageLoader.IsTiff(data))
            return TiffImageLoader.Load(data);

        throw new NotSupportedException("Unsupported image format. Only BMP, PNG, JPEG, and TIFF are currently supported.");
    }

    internal static Image Create(int width, int height, Argb[] pixels)
    {
        return Create(width, height, pixels, highPrecisionSamples: null);
    }

    internal static Image Create(int width, int height, Argb[] pixels, ushort[]? highPrecisionSamples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        if (highPrecisionSamples is not null && highPrecisionSamples.Length != checked(pixels.Length * 4))
            throw new ArgumentOutOfRangeException(nameof(highPrecisionSamples));

        return new Image(width, height, pixels, highPrecisionSamples);
    }

    public bool Equals([NotNullWhen(true)] Image? other)
    {
        if (other is null)
            return false;

        if (Width != other.Width || Height != other.Height)
            return false;

        var expectedPixels = MemoryMarshal.Cast<Argb, uint>(_pixels.AsSpan());
        var actualPixels = MemoryMarshal.Cast<Argb, uint>(other._pixels.AsSpan());
        return PixelsEqual(expectedPixels, actualPixels) && HighPrecisionSamplesEqual(other);
    }

    private static bool PixelsEqual(ReadOnlySpan<uint> expectedPixels, ReadOnlySpan<uint> actualPixels)
    {
        if (expectedPixels.SequenceEqual(actualPixels))
            return true;

        // Encoders store arbitrary color values under a zero alpha, so fully transparent pixels are equal
        // whatever color they hide
        for (var i = 0; i < expectedPixels.Length; i++)
        {
            if (NormalizeTransparentPixel(expectedPixels[i]) != NormalizeTransparentPixel(actualPixels[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Compares the samples that <see cref="Pixels"/> cannot represent. An 8-bit sample <c>v</c> is the 16-bit
    /// sample <c>v * 257</c>, so an 8-bit image equals a 16-bit one only when every 16-bit sample is exactly that.
    /// Fully transparent pixels are equal whatever color they hide, as in <see cref="PixelsEqual"/>.
    /// </summary>
    private bool HighPrecisionSamplesEqual(Image other)
    {
        if (_highPrecisionSamples is null && other._highPrecisionSamples is null)
            return true;

        if (_highPrecisionSamples is not null && other._highPrecisionSamples is not null)
        {
            if (_highPrecisionSamples.AsSpan().SequenceEqual(other._highPrecisionSamples))
                return true;

            for (var i = 0; i < _highPrecisionSamples.Length; i += 4)
            {
                if (_highPrecisionSamples[i] is 0 && other._highPrecisionSamples[i] is 0)
                    continue;

                if (!_highPrecisionSamples.AsSpan(i, 4).SequenceEqual(other._highPrecisionSamples.AsSpan(i, 4)))
                    return false;
            }

            return true;
        }

        var (samples, pixels) = _highPrecisionSamples is not null ? (_highPrecisionSamples, other._pixels) : (other._highPrecisionSamples!, _pixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            if (samples[i * 4] is 0 && pixel.A is 0)
                continue;

            if (samples[i * 4] != pixel.A * 257 || samples[(i * 4) + 1] != pixel.R * 257 || samples[(i * 4) + 2] != pixel.G * 257 || samples[(i * 4) + 3] != pixel.B * 257)
                return false;
        }

        return true;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Image image && Equals(image);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Width);
        hash.Add(Height);
        var hashPixelCount = Math.Min(_pixels.Length, 32);
        foreach (var pixel in _pixels.AsSpan(0, hashPixelCount))
        {
            hash.Add(NormalizeTransparentPixel(pixel.PackedValue));
        }

        return hash.ToHashCode();
    }

    internal static uint NormalizeTransparentPixel(uint pixel) => pixel >> 24 is 0 ? 0 : pixel;

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
        return memoryStream.ToArray();
    }
}
