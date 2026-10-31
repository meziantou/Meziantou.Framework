using System.Globalization;

namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// A quality layer of a JPEG 2000 output (<see cref="Jpeg2000Encoder.QualityLayers"/>): how many bytes the output may have
/// up to the end of this layer, or the rest of the coding passes.
/// </summary>
/// <remarks>
/// A target counts everything a reader needs to decode the layers up to this one: the boxes of a JP2 file, the headers,
/// and the packets of this layer and of the previous ones. The encoder keeps the coding passes that reduce the error most
/// for their bytes (the post-compression rate-distortion optimization of T.800, J.14), so that the size is at most the
/// target, unless the headers and the empty packets alone are larger.
/// </remarks>
public sealed class Jpeg2000QualityLayer
{
    private Jpeg2000QualityLayer(long? size, double? bitsPerPixel)
    {
        Size = size;
        BitsPerPixel = bitsPerPixel;
    }

    /// <summary>Gets a layer that holds every coding pass the previous layers do not hold: the last layer of an output that keeps all of its quality.</summary>
    public static Jpeg2000QualityLayer Complete { get; } = new(size: null, bitsPerPixel: null);

    /// <summary>Gets the largest number of bytes of the output up to this layer, or <see langword="null"/>.</summary>
    public long? Size { get; }

    /// <summary>Gets the largest number of bits per pixel of the output up to this layer, or <see langword="null"/>.</summary>
    public double? BitsPerPixel { get; }

    /// <summary>Gets a value indicating whether the layer holds every coding pass the previous layers do not hold.</summary>
    public bool IsComplete => Size is null && BitsPerPixel is null;

    /// <summary>Creates a layer that ends when the output has at most <paramref name="bytes"/> bytes.</summary>
    /// <param name="bytes">The size of the output up to the end of the layer.</param>
    /// <returns>The layer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytes"/> is not positive.</exception>
    public static Jpeg2000QualityLayer FromSize(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        return new(bytes, bitsPerPixel: null);
    }

    /// <summary>Creates a layer that ends when the output has at most <paramref name="bitsPerPixel"/> bits for each pixel of the image.</summary>
    /// <param name="bitsPerPixel">The size of the output up to the end of the layer, in bits per pixel: 8 times the bytes, divided by the width and the height.</param>
    /// <returns>The layer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitsPerPixel"/> is not a positive finite number.</exception>
    public static Jpeg2000QualityLayer FromBitsPerPixel(double bitsPerPixel)
    {
        if (!double.IsFinite(bitsPerPixel) || bitsPerPixel <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitsPerPixel), bitsPerPixel, "The number of bits per pixel must be a positive finite number.");

        return new(size: null, bitsPerPixel);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (Size is { } size)
            return string.Create(CultureInfo.InvariantCulture, $"{size} bytes");

        if (BitsPerPixel is { } bitsPerPixel)
            return string.Create(CultureInfo.InvariantCulture, $"{bitsPerPixel} bits per pixel");

        return "Complete";
    }

    /// <summary>Gets the largest number of bytes of an output of <paramref name="pixels"/> pixels up to this layer; <see cref="long.MaxValue"/> for <see cref="Complete"/>.</summary>
    internal long GetTargetSize(long pixels)
    {
        if (Size is { } size)
            return size;

        if (BitsPerPixel is { } bitsPerPixel)
            return (long)Math.Min(long.MaxValue / 2, Math.Floor(bitsPerPixel * pixels / 8));

        return long.MaxValue;
    }
}
