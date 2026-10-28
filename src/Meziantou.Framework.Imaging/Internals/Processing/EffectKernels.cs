using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The in-place kernels of the effects of <see cref="ImageProcessingExtensions"/> that are not drawings or color
/// matrices: <see cref="ImageProcessingExtensions.Pixelate(Image, int, CancellationToken)"/>,
/// <see cref="ImageProcessingExtensions.OilPaint(Image, int, int, CancellationToken)"/> and
/// <see cref="ImageProcessingExtensions.Flatten(Image, Rgba64, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// The pixelation and the oil painting work on the working samples of the filters (<see cref="WorkingSamples"/>): the
/// stored values, the colors premultiplied by alpha when the format has alpha, stored back with the rounding of the
/// filters (<c>floor(v + 1/2 + 2^-20)</c>, clamped; a pixel whose alpha rounds to 0 becomes transparent black). Their sums
/// are made in a fixed order, so the result does not depend on the platform. The sums of the oil painting of the integer
/// formats are sums of integers that stay below 2^53 (the radius is limited for that): they are exact, whatever their
/// order, so its window can remove the pixels it leaves.
/// </para>
/// <para>
/// The flattening uses the formulas of the conversions to a format without alpha (<see cref="PixelConverter"/>).
/// </para>
/// <para>
/// <see cref="Rgb48"/> has kernels here. <see cref="La16"/>, <see cref="La32"/> and <see cref="A8"/> run on the working copy
/// of <see cref="ProcessingBridge"/>, whose three colors are equal: the means keep them equal, the oil painting takes the
/// gray as the intensity, and the flattened colors convert back to their luma, as the conversion of the gray format to a
/// format without alpha does. <see cref="A8"/> cannot be flattened.
/// </para>
/// </remarks>
internal static class EffectKernels
{
    /// <summary>Gets the number of doubles of the scratch of <see cref="Pixelate{TPixel}"/>: the working row, the output row and the sums of the blocks of a row.</summary>
    public static long GetPixelateScratchLength(PixelFormat format, int width, int size)
    {
        var channels = PixelFormats.GetComponentCount(format);
        var blocks = ((long)width + size - 1) / size;
        return ((2L * width) + blocks) * channels;
    }

    /// <summary>
    /// Throws a <see cref="NotSupportedException"/> when a pixel format has no kernel here, directly or through
    /// <see cref="ProcessingBridge"/>. Called before anything changes.
    /// </summary>
    public static void EnsureSupported(PixelFormat format)
    {
        if (ProcessingBridge.GetKernelFormat(format) is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Rgba64 or PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.Rgb48 or PixelFormat.RgbaFloat or PixelFormat.RgbaHalf))
            throw new NotSupportedException($"The pixel format '{format}' is not supported by this effect.");
    }

    /// <summary>
    /// Throws a <see cref="NotSupportedException"/> when <see cref="Flatten{TPixel}"/> cannot flatten a pixel format:
    /// those of <see cref="EnsureSupported"/> but <see cref="PixelFormat.A8"/>, an alpha mask that cannot store the colors
    /// of the result (convert it to a format with colors first). Called before anything changes.
    /// </summary>
    public static void EnsureFlattenSupported(PixelFormat format)
    {
        if (format is PixelFormat.A8)
            throw new NotSupportedException($"The pixel format '{format}' cannot be flattened: it has no color to hold the background. Convert the image to a format with colors first.");

        EnsureSupported(format);
    }

    /// <summary>
    /// Replaces every block of <paramref name="size"/> by <paramref name="size"/> pixels, from the top-left corner, by the
    /// mean of its pixels: premultiplied colors and alpha. The blocks of the right and bottom edges have the pixels that
    /// remain.
    /// </summary>
    public static void Pixelate<TPixel>(scoped in PixelLease lease, int size, Span<double> scratch, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var format = PixelFormats.GetPixelFormat<TPixel>();
        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            PixelateCore<ushort>(storage, format, size, scratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            PixelateCore<byte>(storage, format, size, scratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaFloat))
        {
            PixelateCore<float>(storage, format, size, scratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            PixelateCore<Half>(storage, format, size, scratch, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    private static void PixelateCore<TSample>(PixelStorage storage, PixelFormat format, int size, Span<double> scratch, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var width = storage.Width;
        var height = storage.Height;
        var channels = PixelFormats.GetComponentCount(format);
        var alpha = PixelFormats.HasAlpha(format) ? WorkingAlpha.Premultiplied : WorkingAlpha.None;
        var max = WorkingSamples.GetMaxValue(format);
        var rowLength = width * channels;
        var blocks = (width + size - 1) / size;
        var working = scratch[..rowLength];
        var output = scratch.Slice(rowLength, rowLength);
        var sums = scratch.Slice(2 * rowLength, blocks * channels);
        for (var top = 0; top < height; top += size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = Math.Min(size, height - top);
            sums.Clear();
            for (var y = top; y < top + rows; y++)
            {
                WorkingSamples.Load<TSample>(GetSamples<TSample>(storage, y), working, alpha, transfer: 0);
                for (var x = 0; x < width; x++)
                {
                    var sum = sums.Slice(x / size * channels, channels);
                    var pixel = working.Slice(x * channels, channels);
                    for (var c = 0; c < channels; c++)
                    {
                        sum[c] += pixel[c];
                    }
                }
            }

            for (var block = 0; block < blocks; block++)
            {
                var left = block * size;
                var columns = Math.Min(size, width - left);
                var count = (double)rows * columns;
                var sum = sums.Slice(block * channels, channels);
                for (var x = left; x < left + columns; x++)
                {
                    var pixel = output.Slice(x * channels, channels);
                    for (var c = 0; c < channels; c++)
                    {
                        pixel[c] = sum[c] / count;
                    }
                }
            }

            // Every row of the band gets the same pixels
            var first = GetSamples<TSample>(storage, top);
            WorkingSamples.Store<TSample>(output, first, alpha, transfer: 0, max);
            for (var y = top + 1; y < top + rows; y++)
            {
                first.CopyTo(GetSamples<TSample>(storage, y));
            }
        }
    }

    /// <summary>Gets the number of doubles of the scratch of <see cref="OilPaint{TPixel}"/>: the ring of working rows, the output row and the sums of the levels.</summary>
    public static long GetOilPaintScratchLength(PixelFormat format, int width, int radius, int levels)
    {
        var channels = PixelFormats.GetComponentCount(format);
        return ((((2L * radius) + 2) * width) + levels) * channels;
    }

    /// <summary>Gets the number of integers of the scratch of <see cref="OilPaint{TPixel}"/>: the levels of the ring of rows and the count of every level.</summary>
    public static long GetOilPaintLevelsLength(int width, int radius, int levels) => (((2L * radius) + 1) * width) + levels;

    /// <summary>
    /// Gives every pixel the mean of the pixels of its neighborhood (the square of side <c>2 radius + 1</c>, inside the
    /// image) whose intensity level is the most frequent one there: the oil painting effect. The intensity of a pixel is
    /// the Rec. 709 luma of its color over black (premultiplied), as a fraction of the largest sample, and its level is
    /// <c>floor(intensity levels)</c>, kept between 0 and <c>levels - 1</c>. The most frequent level with the lowest index
    /// wins a tie, and the mean is that of the premultiplied colors and of the alpha of its pixels.
    /// </summary>
    /// <remarks>
    /// The window moves along the row, one column in and one column out, for the integer formats, whose sums are exact;
    /// for the floating-point formats, the sums of every window are made again from its pixels, so that a sample that is
    /// not a number or an infinity only reaches the windows that contain it.
    /// </remarks>
    public static void OilPaint<TPixel>(scoped in PixelLease lease, int levels, int radius, bool grayColors, Span<double> scratch, Span<int> levelScratch, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var format = PixelFormats.GetPixelFormat<TPixel>();
        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            OilPaintCore<ushort>(storage, format, levels, radius, blueFirst: false, grayColors, scratch, levelScratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            OilPaintCore<byte>(storage, format, levels, radius, blueFirst: typeof(TPixel) == typeof(Bgra32), grayColors, scratch, levelScratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaFloat))
        {
            OilPaintCore<float>(storage, format, levels, radius, blueFirst: false, grayColors, scratch, levelScratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            OilPaintCore<Half>(storage, format, levels, radius, blueFirst: false, grayColors, scratch, levelScratch, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    private static void OilPaintCore<TSample>(PixelStorage storage, PixelFormat format, int levels, int radius, bool blueFirst, bool grayColors, Span<double> scratch, Span<int> levelScratch, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var width = storage.Width;
        var height = storage.Height;
        var hasAlpha = PixelFormats.HasAlpha(format);
        var max = WorkingSamples.GetMaxValue(format);

        // The intensity of a working pixel, as a fraction: premultiplied colors are up to max * max (max for a format
        // without alpha, and 1 for the floating-point formats)
        var window = new OilPaintWindow
        {
            Width = width,
            Channels = PixelFormats.GetComponentCount(format),
            Diameter = (2 * radius) + 1,
            Levels = levels,
            Alpha = hasAlpha ? WorkingAlpha.Premultiplied : WorkingAlpha.None,
            Scale = WorkingSamples.IsFloatingPoint<TSample>() ? 1.0 : hasAlpha ? (double)max * max : max,
            LumaR = blueFirst ? 0.0722 : 0.2126,
            LumaB = blueFirst ? 0.2126 : 0.0722,
            GrayColors = grayColors,
        };

        var rowLength = width * window.Channels;
        var ring = scratch[..(window.Diameter * rowLength)];
        var output = scratch.Slice(window.Diameter * rowLength, rowLength);
        var sums = scratch.Slice((window.Diameter + 1) * rowLength, levels * window.Channels);
        var ringLevels = levelScratch[..(window.Diameter * width)];
        var counts = levelScratch.Slice(window.Diameter * width, levels);
        var sliding = !WorkingSamples.IsFloatingPoint<TSample>();

        // The rows of the first window. Every row is read before it is written: the window of a row reaches the rows
        // below it, and the slot of a row in the ring is reused once no window reads it
        for (var y = 0; y < Math.Min(radius, height); y++)
        {
            LoadOilPaintRow(GetSamples<TSample>(storage, y), y, window, ring, ringLevels);
        }

        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (y + radius < height)
            {
                LoadOilPaintRow(GetSamples<TSample>(storage, y + radius), y + radius, window, ring, ringLevels);
            }

            var top = Math.Max(0, y - radius);
            var bottom = Math.Min(height - 1, y + radius);
            for (var x = 0; x < width; x++)
            {
                if (!sliding || x == 0)
                {
                    sums.Clear();
                    counts.Clear();
                    for (var column = Math.Max(0, x - radius); column <= Math.Min(width - 1, x + radius); column++)
                    {
                        AddOilPaintColumn(column, top, bottom, 1, window, ring, ringLevels, sums, counts);
                    }
                }
                else
                {
                    if (x - radius - 1 >= 0)
                    {
                        AddOilPaintColumn(x - radius - 1, top, bottom, -1, window, ring, ringLevels, sums, counts);
                    }

                    if (x + radius < width)
                    {
                        AddOilPaintColumn(x + radius, top, bottom, 1, window, ring, ringLevels, sums, counts);
                    }
                }

                var best = 0;
                for (var level = 1; level < levels; level++)
                {
                    if (counts[level] > counts[best])
                    {
                        best = level;
                    }
                }

                var sum = sums.Slice(best * window.Channels, window.Channels);
                var pixel = output.Slice(x * window.Channels, window.Channels);
                for (var c = 0; c < window.Channels; c++)
                {
                    pixel[c] = sum[c] / counts[best];
                }
            }

            WorkingSamples.Store<TSample>(output, GetSamples<TSample>(storage, y), window.Alpha, transfer: 0, max);
        }
    }

    /// <summary>Loads a row into its slot of the ring, with the intensity level of each of its pixels.</summary>
    private static void LoadOilPaintRow<TSample>(ReadOnlySpan<TSample> samples, int y, in OilPaintWindow window, Span<double> ring, Span<int> ringLevels)
        where TSample : unmanaged
    {
        var slot = y % window.Diameter;
        var channels = window.Channels;
        var working = ring.Slice(slot * window.Width * channels, window.Width * channels);
        WorkingSamples.Load(samples, working, window.Alpha, transfer: 0);
        var rowLevels = ringLevels.Slice(slot * window.Width, window.Width);
        var levels = window.Levels;
        for (var x = 0; x < window.Width; x++)
        {
            var pixel = working.Slice(x * channels, channels);
            var intensity = channels < 3 || window.GrayColors ? pixel[0] : (window.LumaR * pixel[0]) + (0.7152 * pixel[1]) + (window.LumaB * pixel[2]);
            var level = intensity / window.Scale * levels;

            // Written so that a sample that is not a number gets the first level
            rowLevels[x] = level >= levels - 1 ? levels - 1 : level > 0 ? (int)level : 0;
        }
    }

    /// <summary>Adds the pixels of a column of the window to the counts and the sums of their levels, or removes them (<paramref name="sign"/> of -1).</summary>
    private static void AddOilPaintColumn(int column, int top, int bottom, int sign, in OilPaintWindow window, ReadOnlySpan<double> ring, ReadOnlySpan<int> ringLevels, Span<double> sums, Span<int> counts)
    {
        var channels = window.Channels;
        for (var v = top; v <= bottom; v++)
        {
            var slot = v % window.Diameter;
            var level = ringLevels[(slot * window.Width) + column];
            counts[level] += sign;
            var sum = sums.Slice(level * channels, channels);
            var pixel = ring.Slice((((slot * window.Width) + column) * channels), channels);
            for (var c = 0; c < channels; c++)
            {
                sum[c] += sign * pixel[c];
            }
        }
    }

    /// <summary>The layout of the window of the oil painting.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct OilPaintWindow
    {
        public int Width { get; init; }

        public int Channels { get; init; }

        public int Diameter { get; init; }

        public int Levels { get; init; }

        public WorkingAlpha Alpha { get; init; }

        public double Scale { get; init; }

        public double LumaR { get; init; }

        public double LumaB { get; init; }

        /// <summary>Gets a value indicating whether the three colors are equal (the working copy of a gray format): the intensity is the first one, exactly.</summary>
        public bool GrayColors { get; init; }
    }

    /// <summary>
    /// Composites every pixel over an opaque background, in place, and makes it opaque, with the formulas of the
    /// conversion to a format without alpha: <c>(s a + bg (max - a) + max / 2) / max</c> for the integer formats (the
    /// background reduced to their precision first), <c>c a + bg (1 - a)</c> in double precision for the floating-point
    /// ones (alpha kept between 0 and 1). A format without alpha is left as it is.
    /// </summary>
    public static void Flatten<TPixel>(scoped in PixelLease lease, Rgba64 background, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (typeof(TPixel) != typeof(Rgba32) && typeof(TPixel) != typeof(Bgra32) && typeof(TPixel) != typeof(Rgba64) && typeof(TPixel) != typeof(RgbaFloat) && typeof(TPixel) != typeof(RgbaHalf))
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");

        Span<Rgb24> opaque = stackalloc Rgb24[64];
        var (bgR, bgG, bgB) = (background.R / (double)ushort.MaxValue, background.G / (double)ushort.MaxValue, background.B / (double)ushort.MaxValue);
        var height = lease.Height;
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRow<TPixel>(y);
            if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
            {
                // The conversion to Rgb24 flattens the 8-bit samples, and the conversion back adds an opaque alpha
                for (var start = 0; start < row.Length; start += opaque.Length)
                {
                    var pixels = row.Slice(start, Math.Min(opaque.Length, row.Length - start));
                    var colors = opaque[..pixels.Length];
                    PixelConverter.ConvertRow<TPixel, Rgb24>(pixels, colors, background);
                    PixelConverter.ConvertRow<Rgb24, TPixel>(colors, pixels);
                }
            }
            else if (typeof(TPixel) == typeof(Rgba64))
            {
                foreach (ref var pixel in unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(row)))
                {
                    if (pixel.A != ushort.MaxValue)
                    {
                        uint a = pixel.A;
                        pixel = new Rgba64(
                            (ushort)PixelConverter.Flatten(pixel.R, background.R, a, ushort.MaxValue),
                            (ushort)PixelConverter.Flatten(pixel.G, background.G, a, ushort.MaxValue),
                            (ushort)PixelConverter.Flatten(pixel.B, background.B, a, ushort.MaxValue));
                    }
                }
            }
            else
            {
                foreach (ref var pixel in row)
                {
                    PixelConverter.ReadFloat(ref pixel, out var r, out var g, out var b, out var a);
                    if (a == 1)
                        continue;

                    a = Math.Clamp(a, 0, 1);
                    r = (r * a) + (bgR * (1 - a));
                    g = (g * a) + (bgG * (1 - a));
                    b = (b * a) + (bgB * (1 - a));
                    if (typeof(TPixel) == typeof(RgbaFloat))
                    {
                        unsafe { Unsafe.As<TPixel, RgbaFloat>(ref pixel) = new RgbaFloat((float)r, (float)g, (float)b, 1); }
                    }
                    else
                    {
                        unsafe { Unsafe.As<TPixel, RgbaHalf>(ref pixel) = new RgbaHalf((Half)r, (Half)g, (Half)b, Half.One); }
                    }
                }
            }
        }
    }

    private static Span<TSample> GetSamples<TSample>(PixelStorage storage, int y)
        where TSample : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(y)));
}
