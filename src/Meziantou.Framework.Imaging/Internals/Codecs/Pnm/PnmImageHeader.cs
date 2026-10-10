using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The resolved header of one Netpbm image: dimensions, sample precision, tuple type and decoded representation.</summary>
/// <remarks>
/// <para>
/// A PBM bit is inverted (1 is black); every other sample is an intensity between 0 and <c>MAXVAL</c>. Samples are scaled
/// to the full range of the decoded representation: 8-bit output for <c>MAXVAL</c> up to 255 and 16-bit output above, so a
/// 16-bit file never loses precision and an unusual <c>MAXVAL</c> (for example 1000) is normalized exactly
/// (<c>round(value * targetMax / MAXVAL)</c>).
/// </para>
/// <para>
/// The library has no gray-with-alpha pixel format, so <c>GRAYSCALE_ALPHA</c> and <c>BLACKANDWHITE_ALPHA</c> tuples are
/// expanded to RGBA (R = G = B), which is lossless.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct PnmImageHeader
{
    public PnmImageHeader(PnmVariant variant, int width, int height, int maxValue, PnmTupleType tupleType)
    {
        Variant = variant;
        Width = width;
        Height = height;
        MaxValue = maxValue;
        TupleType = tupleType;
    }

    /// <summary>Gets the magic number of the file.</summary>
    public PnmVariant Variant { get; }

    /// <summary>Gets the positive width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the positive height, in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets <c>MAXVAL</c> (1 to 65,535; 1 for PBM).</summary>
    public int MaxValue { get; }

    /// <summary>Gets the tuple type.</summary>
    public PnmTupleType TupleType { get; }

    /// <summary>Gets a value indicating whether the raster is plain (ASCII) text.</summary>
    public bool IsPlain => Variant is PnmVariant.PlainBitmap or PnmVariant.PlainGrayMap or PnmVariant.PlainPixMap;

    /// <summary>Gets a value indicating whether the raster is packed bits (PBM).</summary>
    public bool IsBitmap => Variant is PnmVariant.PlainBitmap or PnmVariant.BinaryBitmap;

    /// <summary>Gets the number of samples of one pixel.</summary>
    public int Channels => TupleType switch
    {
        PnmTupleType.BlackAndWhite or PnmTupleType.Grayscale => 1,
        PnmTupleType.BlackAndWhiteAlpha or PnmTupleType.GrayscaleAlpha => 2,
        PnmTupleType.Rgb => 3,
        _ => 4,
    };

    /// <summary>Gets a value indicating whether the tuple has an alpha sample.</summary>
    public bool HasAlpha => TupleType is PnmTupleType.BlackAndWhiteAlpha or PnmTupleType.GrayscaleAlpha or PnmTupleType.RgbAlpha;

    /// <summary>Gets a value indicating whether the color samples are grayscale.</summary>
    public bool IsGrayscale => TupleType is PnmTupleType.BlackAndWhite or PnmTupleType.Grayscale or PnmTupleType.BlackAndWhiteAlpha or PnmTupleType.GrayscaleAlpha;

    /// <summary>Gets the number of bytes of one stored sample (1 up to <c>MAXVAL</c> 255, otherwise 2, big-endian).</summary>
    public int BytesPerSample => MaxValue <= 255 ? 1 : 2;

    /// <summary>Gets the number of samples of one row.</summary>
    public int SamplesPerRow => Width * Channels;

    /// <summary>Gets the number of bytes of one stored row (packed bits for PBM).</summary>
    public int StoredRowLength => IsBitmap ? (Width + 7) / 8 : Width * Channels * BytesPerSample;

    /// <summary>Gets a value indicating whether the decoded samples are 16-bit.</summary>
    public bool Is16Bit => MaxValue > 255;

    /// <summary>Gets the largest decoded sample value (255 or 65,535).</summary>
    public int TargetMaxValue => Is16Bit ? 65535 : 255;

    /// <summary>Gets the decoded representation.</summary>
    public PixelFormat PixelFormat => DefaultPixelFormats.ForPnm(IsGrayscale, HasAlpha, Is16Bit);

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel => (IsGrayscale, HasAlpha) switch
    {
        (true, false) => ImageColorModel.Grayscale,
        (true, true) => ImageColorModel.GrayscaleAlpha,
        (false, false) => ImageColorModel.Rgb,
        _ => ImageColorModel.Rgba,
    };

    /// <summary>Gets the reported precision: the smallest of 1, 2, 4, 8 and 16 bits that holds <see cref="MaxValue"/>.</summary>
    public int BitsPerComponent => MaxValue switch
    {
        1 => 1,
        <= 3 => 2,
        <= 15 => 4,
        <= 255 => 8,
        _ => 16,
    };
}
