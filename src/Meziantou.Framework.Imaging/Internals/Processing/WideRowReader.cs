using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the rows of a storage as RGBA doubles for the comparisons at a precision above 8 bits (<see cref="ImageSimilarity"/>):
/// 16-bit samples (0 to 65,535), converted like <see cref="Image.CloneAs{TPixel}"/> converts to <see cref="Rgba64"/>, or
/// fractions of the sample range, the stored values of the floating-point formats.
/// </summary>
internal sealed class WideRowReader : IDisposable
{
    private readonly PixelFormat _format;
    private readonly SimilarityPrecision _precision;
    private PooledBuffer? _converted;
    private PooledBuffer? _values;

    private WideRowReader(PixelFormat format, SimilarityPrecision precision, PooledBuffer? converted, PooledBuffer values)
    {
        _format = format;
        _precision = precision;
        _converted = converted;
        _values = values;
    }

    /// <summary>Rents the buffers of a row.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public static WideRowReader Create(AllocationScope scope, PixelFormat format, int width, SimilarityPrecision precision)
    {
        if (precision is not (SimilarityPrecision.SixteenBit or SimilarityPrecision.FloatingPoint))
            throw new ArgumentOutOfRangeException(nameof(precision), precision, "The precision is not read by this reader.");

        var direct = format == PixelFormat.Rgba64 || (precision == SimilarityPrecision.FloatingPoint && format is PixelFormat.RgbaFloat or PixelFormat.RgbaHalf);
        var converted = direct ? null : ResampleWeights.Rent<Rgba64>(scope, width);
        try
        {
            return new WideRowReader(format, precision, converted, ResampleWeights.Rent<double>(scope, 4L * width));
        }
        catch
        {
            converted?.Dispose();
            throw;
        }
    }

    /// <summary>Gets the largest value of a color sample of a precision (the range of the samples).</summary>
    public static double GetColorRange(SimilarityPrecision precision, double dataRange) => precision == SimilarityPrecision.SixteenBit ? ushort.MaxValue : dataRange;

    /// <summary>Gets the largest value of an alpha sample of a precision.</summary>
    public static double GetAlphaRange(SimilarityPrecision precision) => precision == SimilarityPrecision.SixteenBit ? ushort.MaxValue : 1;

    /// <summary>
    /// Premultiplies a color by an alpha: rounded to the nearest 16-bit sample like the 8-bit comparisons round theirs, or
    /// the exact product of fractions.
    /// </summary>
    public static double Premultiply(double value, double alpha, SimilarityPrecision precision)
        => precision == SimilarityPrecision.SixteenBit ? (((ulong)value * (ulong)alpha) + 32767) / 65535 : value * alpha;

    /// <summary>Gets row <paramref name="y"/> of a storage of the format given at creation, leased by the caller: 4 values per pixel, valid until the next call.</summary>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or not a number (16 bits), or is not finite (fractions).</exception>
    public ReadOnlySpan<double> GetRow(PixelStorage storage, int y)
    {
        var width = storage.Width;
        var row = storage.GetRowSpanCore(y)[..(width * PixelFormats.GetBytesPerPixel(_format))];
        var values = unsafe(MemoryMarshal.Cast<byte, double>((_values ?? throw new ObjectDisposedException(nameof(WideRowReader))).Span))[..(4 * width)];
        var requirement = _precision == SimilarityPrecision.SixteenBit ? FloatingPointSampleRequirement.UnitSamples : FloatingPointSampleRequirement.Finite;
        if (FloatingPointSampleRange.TryFind(_format, row, requirement, out var x, out var pixel))
        {
            var expected = _precision == SimilarityPrecision.SixteenBit ? "a number in [0, 1], which has a 16-bit value" : "a finite number";
            throw new UnsupportedImageFeatureException(
                string.Create(CultureInfo.InvariantCulture, $"Image comparison reads every sample as {expected}, but the {_format} pixel ({x}, {y}) is ({pixel.R}, {pixel.G}, {pixel.B}, {pixel.A}). Convert the image with Image.CloneAs and PixelConversionOptions.OutOfRangeSamples or PixelConversionOptions.ToneMapping first."),
                ImageFormat.Unknown,
                "Comparison of floating-point samples out of range");
        }

        if (_precision == SimilarityPrecision.FloatingPoint && _format == PixelFormat.RgbaFloat)
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, float>(row));
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = samples[i];
            }

            return values;
        }

        if (_precision == SimilarityPrecision.FloatingPoint && _format == PixelFormat.RgbaHalf)
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, Half>(row));
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (double)samples[i];
            }

            return values;
        }

        ReadOnlySpan<ushort> wide;
        if (_format == PixelFormat.Rgba64)
        {
            wide = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
        }
        else
        {
            var buffer = (_converted ?? throw new ObjectDisposedException(nameof(WideRowReader))).Span;
            _ = PixelConverter.ConvertRow(_format, row, PixelFormat.Rgba64, buffer);
            wide = unsafe(MemoryMarshal.Cast<byte, ushort>(buffer))[..(4 * width)];
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = _precision == SimilarityPrecision.SixteenBit ? wide[i] : wide[i] / (double)ushort.MaxValue;
        }

        return values;
    }

    public void Dispose()
    {
        _converted?.Dispose();
        _converted = null;
        _values?.Dispose();
        _values = null;
    }
}
