using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Finds the floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) that an operation cannot read: a
/// transfer function decodes [0, 1] (linear-light filtering of encoded samples), an ICC profile encodes [0, 1]
/// (<see cref="ImageProcessingExtensions.ConvertColorProfile"/>), the comparisons read 8-bit samples
/// (<see cref="ImageSimilarity"/>), and the analysis of an auto-crop needs numbers. The operations check the pixels before
/// they change or compute anything, and reject the samples they cannot read instead of clipping them.
/// </summary>
internal static class FloatingPointSampleRange
{
    /// <summary>
    /// Finds the first pixel of a frame that does not meet <paramref name="requirement"/>. Integer formats have no such
    /// pixel.
    /// </summary>
    /// <param name="lease">The leased frame.</param>
    /// <param name="format">The pixel format of the frame.</param>
    /// <param name="requirement">What the samples must be.</param>
    /// <param name="sample">Receives the pixel, when there is one.</param>
    /// <returns><see langword="true"/> if a pixel does not meet the requirement.</returns>
    public static bool TryFind(scoped in PixelLease lease, PixelFormat format, FloatingPointSampleRequirement requirement, out OutOfRangeSample sample)
    {
        if (PixelFormats.IsFloatingPoint(format))
        {
            for (var y = 0; y < lease.Height; y++)
            {
                if (TryFind(format, lease.GetRowBytes(y), requirement, out var x, out var pixel))
                {
                    sample = new OutOfRangeSample(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
                    return true;
                }
            }
        }

        sample = default;
        return false;
    }

    /// <summary>Finds the first pixel of a row that does not meet <paramref name="requirement"/> (integer formats have none).</summary>
    /// <param name="format">The pixel format of the row.</param>
    /// <param name="row">The pixels.</param>
    /// <param name="requirement">What the samples must be.</param>
    /// <param name="index">Receives the index of the pixel, or -1.</param>
    /// <param name="pixel">Receives the pixel.</param>
    /// <returns><see langword="true"/> if a pixel does not meet the requirement.</returns>
    public static bool TryFind(PixelFormat format, ReadOnlySpan<byte> row, FloatingPointSampleRequirement requirement, out int index, out RgbaFloat pixel)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, RgbaFloat>(row));
            for (var i = 0; i < pixels.Length; i++)
            {
                var value = pixels[i];
                if (!Meets(value.R, value.G, value.B, value.A, requirement))
                {
                    (index, pixel) = (i, value);
                    return true;
                }
            }
        }
        else if (format == PixelFormat.RgbaHalf)
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, RgbaHalf>(row));
            for (var i = 0; i < pixels.Length; i++)
            {
                var value = new RgbaFloat((float)pixels[i].R, (float)pixels[i].G, (float)pixels[i].B, (float)pixels[i].A);
                if (!Meets(value.R, value.G, value.B, value.A, requirement))
                {
                    (index, pixel) = (i, value);
                    return true;
                }
            }
        }

        (index, pixel) = (-1, default);
        return false;
    }

    // A sample that is not a number fails every comparison
    private static bool Meets(float r, float g, float b, float a, FloatingPointSampleRequirement requirement) => requirement switch
    {
        FloatingPointSampleRequirement.UnitColors => r is >= 0 and <= 1 && g is >= 0 and <= 1 && b is >= 0 and <= 1,
        FloatingPointSampleRequirement.UnitSamples => r is >= 0 and <= 1 && g is >= 0 and <= 1 && b is >= 0 and <= 1 && a is >= 0 and <= 1,
        _ => float.IsFinite(r) && float.IsFinite(g) && float.IsFinite(b) && float.IsFinite(a),
    };

    /// <summary>A pixel that does not meet a requirement: its position and its samples (exact, a half widened to a float).</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct OutOfRangeSample(int X, int Y, float R, float G, float B, float A)
    {
        /// <summary>Describes the pixel for an exception message: <c>(r, g, b, a)</c>.</summary>
        public string Describe() => string.Create(CultureInfo.InvariantCulture, $"({R}, {G}, {B}, {A})");
    }
}
