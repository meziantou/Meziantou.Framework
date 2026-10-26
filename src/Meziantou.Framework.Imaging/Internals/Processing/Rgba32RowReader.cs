using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the rows of a leased storage as <see cref="Rgba32"/> pixels, for the analyses that work on 8-bit RGBA whatever the
/// pixel format (<see cref="ImageSimilarity"/>). Rows already stored as <see cref="Rgba32"/> are returned as they are; the
/// others are converted like <see cref="Image.CloneAs{TPixel}"/> does into a row buffer rented from the scope.
/// </summary>
internal sealed class Rgba32RowReader : IDisposable
{
    private readonly PixelFormat _format;
    private PooledBuffer? _buffer;

    private Rgba32RowReader(PixelFormat format, PooledBuffer? buffer)
    {
        _format = format;
        _buffer = buffer;
    }

    /// <summary>Rents the conversion buffer of a row, when the format needs one.</summary>
    /// <param name="scope">The scope charged for the buffer.</param>
    /// <param name="format">The pixel format of the storage.</param>
    /// <param name="width">The width of the rows.</param>
    /// <exception cref="ImageResourceLimitException">The buffer exceeds the allocation limit.</exception>
    public static Rgba32RowReader Create(AllocationScope scope, PixelFormat format, int width)
    {
        var buffer = format is PixelFormat.Rgba32 ? null : ResampleWeights.Rent<Rgba32>(scope, width);
        return new Rgba32RowReader(format, buffer);
    }

    /// <summary>Gets row <paramref name="y"/> of a lease on a storage of the format given at creation.</summary>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample of the row is not a number in [0, 1]: it has no 8-bit value, and it is not clipped.</exception>
    public ReadOnlySpan<Rgba32> GetRow(PixelLease lease, int y)
    {
        if (_format is PixelFormat.Rgba32)
            return lease.GetRow<Rgba32>(y);

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(Rgba32RowReader));
        var row = lease.GetRowBytes(y);
        if (FloatingPointSampleRange.TryFind(_format, row, FloatingPointSampleRequirement.UnitSamples, out var x, out var pixel))
            throw new UnsupportedImageFeatureException(
                string.Create(CultureInfo.InvariantCulture, $"Image comparison reads the pixels as 8-bit samples, but the {_format} pixel ({x}, {y}) is ({pixel.R}, {pixel.G}, {pixel.B}, {pixel.A}): a sample outside [0, 1], or that is not a number, has no 8-bit value, and it is not clipped implicitly. Convert the image with Image.CloneAs and PixelConversionOptions.OutOfRangeSamples or PixelConversionOptions.ToneMapping first."),
                ImageFormat.Unknown,
                "Comparison of floating-point samples outside [0, 1]");

        var width = PixelConverter.ConvertRow(_format, row, PixelFormat.Rgba32, buffer.Span);
        return unsafe(MemoryMarshal.Cast<byte, Rgba32>(buffer.Span))[..width];
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
