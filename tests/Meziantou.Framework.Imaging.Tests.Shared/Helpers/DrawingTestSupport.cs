using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Shared support of the drawing tests: images of any pixel format, their samples, and the comparison with the references of the test harness.</summary>
internal static class DrawingTestSupport
{
    public static TheoryData<PixelFormat> AllFormats => [.. PixelFormats.All];

    /// <summary>Creates an image whose pixels are zero (transparent black, or black).</summary>
    public static Image Create(PixelFormat format, int width, int height, ImageConfiguration? configuration = null) => format switch
    {
        PixelFormat.Rgba32 => new Image<Rgba32>(width, height, configuration),
        PixelFormat.Bgra32 => new Image<Bgra32>(width, height, configuration),
        PixelFormat.Rgb24 => new Image<Rgb24>(width, height, configuration),
        PixelFormat.Rgba64 => new Image<Rgba64>(width, height, configuration),
        PixelFormat.Gray8 => new Image<Gray8>(width, height, configuration),
        PixelFormat.Gray16 => new Image<Gray16>(width, height, configuration),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Creates an image with random pixels; about one pixel out of four is fully transparent and one out of four fully opaque when the format has alpha.</summary>
    /// <param name="format">The pixel format.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="next">Returns a random integer from 0 to its argument (excluded).</param>
    public static Image CreateRandom(PixelFormat format, int width, int height, Func<int, int> next)
    {
        var image = Create(format, width, height);
        var layout = ReferencePixelLayout.FromPixelFormat(format);
        var samples = new int[width * height * layout.ChannelCount];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = next(layout.MaxValue + 1);
        }

        if (layout.HasAlpha)
        {
            for (var i = layout.ColorChannels; i < samples.Length; i += layout.ChannelCount)
            {
                samples[i] = next(4) switch
                {
                    0 => 0,
                    1 => layout.MaxValue,
                    _ => samples[i],
                };
            }
        }

        WriteSamples(image.Frames[0], samples);
        return image;
    }

    /// <summary>Reads every sample of a frame, row by row, in storage order.</summary>
    public static int[] ReadSamples(ImageFrame frame)
    {
        var layout = ReferencePixelLayout.FromPixelFormat(frame.PixelFormat);
        var samples = new int[frame.Width * frame.Height * layout.ChannelCount];
        frame.ProcessPixelBytes((samples, layout), static (pixels, state) =>
        {
            var index = 0;
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                if (state.layout.MaxValue == 255)
                {
                    foreach (var value in row)
                    {
                        state.samples[index++] = value;
                    }
                }
                else
                {
                    foreach (var value in unsafe(MemoryMarshal.Cast<byte, ushort>(row)))
                    {
                        state.samples[index++] = value;
                    }
                }
            }
        });
        return samples;
    }

    /// <summary>Writes every sample of a frame, row by row, in storage order.</summary>
    public static void WriteSamples(ImageFrame frame, int[] samples)
    {
        var layout = ReferencePixelLayout.FromPixelFormat(frame.PixelFormat);
        frame.ProcessPixelBytes((samples, layout), static (pixels, state) =>
        {
            var index = 0;
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                if (state.layout.MaxValue == 255)
                {
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] = (byte)state.samples[index++];
                    }
                }
                else
                {
                    var wide = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                    for (var i = 0; i < wide.Length; i++)
                    {
                        wide[i] = (ushort)state.samples[index++];
                    }
                }
            }
        });
    }

    /// <summary>Gets an approximation of an exact value, for messages (the exact values of arbitrary doubles do not fit a decimal).</summary>
    public static double ToDouble(Fraction value)
        => (double)(value.Numerator * (System.Numerics.BigInteger.One << 64) / value.Denominator) / 18446744073709551616d;

    public static ReferencePaint ToPaint(Rgba64 color, PixelFormat format)
        => ReferencePaint.Convert(color.R, color.G, color.B, color.A, ReferencePixelLayout.FromPixelFormat(format));

    public static VectorPath ToPath(IEnumerable<IReadOnlyList<(double X, double Y)>> polygons)
    {
        var builder = new VectorPathBuilder();
        foreach (var polygon in polygons)
        {
            builder.AddPolygon([.. polygon.Select(point => new PointD(point.X, point.Y))]);
        }

        return builder.Build();
    }

    /// <summary>
    /// Checks the pixels of a frame against the exact areas of a shape painted with one color over known pixels: each
    /// pixel must be the reference compositing of the paint for an accepted coverage (the correctly rounded area, or
    /// either neighbor within the tie window).
    /// </summary>
    /// <returns>A description of the first mismatches, or <see langword="null"/> when every pixel matches.</returns>
    public static string? Compare(ImageFrame frame, int[] before, Fraction[] areas, ReferencePaint paint)
        => Compare(frame, before, areas, paint, mode: null);

    /// <summary>The same for a paint combined with an operator of Porter and Duff, checked against the reference of the operators.</summary>
    public static string? Compare(ImageFrame frame, int[] before, Fraction[] areas, ReferencePaint paint, ReferenceCompositeMode? mode)
    {
        var layout = ReferencePixelLayout.FromPixelFormat(frame.PixelFormat);
        var after = ReadSamples(frame);
        var channels = layout.ChannelCount;
        var mismatches = new List<string>();
        for (var pixel = 0; pixel < areas.Length; pixel++)
        {
            var destination = before.AsSpan(pixel * channels, channels).ToArray();
            var actual = after.AsSpan(pixel * channels, channels).ToArray();
            var (low, high) = ReferenceCoverage.GetAcceptedCoverage(areas[pixel]);
            var expected = Blend(low);
            if (actual.SequenceEqual(expected))
                continue;

            if (high != low && actual.SequenceEqual(Blend(high)))
                continue;

            int[] Blend(int coverage)
                => mode is { } value ? ReferenceCompositor.Blend(destination, paint, coverage, layout, value) : ReferenceCompositor.Blend(destination, paint, coverage, layout);

            if (mismatches.Count < 8)
            {
                mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"pixel ({pixel % frame.Width}, {pixel / frame.Width}): area {ToDouble(areas[pixel])} (coverage {low}..{high}), before [{string.Join(' ', destination)}], expected [{string.Join(' ', expected)}], actual [{string.Join(' ', actual)}]"));
            }
        }

        return mismatches.Count == 0 ? null : string.Join(Environment.NewLine, mismatches);
    }
}
