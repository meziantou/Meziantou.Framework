using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
/// Large frames are processed by up to <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers, by row bands
/// (<see cref="EffectScratch"/> for the pixelation and the oil painting): every pixel is computed as by one worker.
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
    public static void Pixelate<TPixel>(scoped in PixelLease lease, int size, EffectScratch scratch, CancellationToken cancellationToken)
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

    private static void PixelateCore<TSample>(PixelStorage storage, PixelFormat format, int size, EffectScratch scratch, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        // The bands start on the first row of a block
        if (scratch.Workers is 1)
        {
            PixelateBand<TSample>(storage, format, size, scratch.GetDoubles(0), 0, storage.Height, cancellationToken);
            return;
        }

        RowBands.RunParallel(scratch.Workers, band => PixelateBand<TSample>(storage, format, size, scratch.GetDoubles(band), scratch.GetBandStart(band), scratch.GetBandStart(band + 1), cancellationToken));
    }

    private static void PixelateBand<TSample>(PixelStorage storage, PixelFormat format, int size, Span<double> scratch, int start, int end, CancellationToken cancellationToken)
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
        for (var top = start; top < end; top += size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = Math.Min(size, height - top);
            sums.Clear();
            for (var y = top; y < top + rows; y++)
            {
                WorkingSamples.Load<TSample>(GetSamples<TSample>(storage, y), working, alpha, transfer: 0);

                // Each block adds its pixels from left to right, as one loop over the row would
                for (var block = 0; block < blocks; block++)
                {
                    var left = block * size;
                    var pixels = working[(left * channels)..(Math.Min(left + size, width) * channels)];
                    AddPixels(pixels, sums.Slice(block * channels, channels));
                }
            }

            // Every pixel of a block gets the same mean
            for (var block = 0; block < blocks; block++)
            {
                var left = block * size;
                var columns = Math.Min(size, width - left);
                var count = (double)rows * columns;
                var sum = sums.Slice(block * channels, channels);
                for (var c = 0; c < channels; c++)
                {
                    sum[c] /= count;
                }

                var pixels = output.Slice(left * channels, columns * channels);
                for (var x = 0; x < pixels.Length; x += channels)
                {
                    sum.CopyTo(pixels[x..]);
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

    /// <summary>
    /// The number of sets of sums and counts of the oil painting of the integer formats: the rows of the window go to the
    /// sets in turn, so that the updates of consecutive rows of the same level do not wait for each other, and the result
    /// adds up the two sets of its level (exactly: the sums are integers).
    /// </summary>
    private const int OilPaintSets = 2;

    /// <summary>Adds pixels of working samples to a sum, one pixel after the other.</summary>
    private static void AddPixels(ReadOnlySpan<double> pixels, Span<double> sum)
    {
        var channels = sum.Length;
        if (channels == 4 && Vector128.IsHardwareAccelerated)
        {
            var low = Vector128.Create(sum);
            var high = Vector128.Create(sum[2..]);
            for (var x = 0; x < pixels.Length; x += 4)
            {
                var pixel = pixels.Slice(x, 4);
                low += Vector128.Create(pixel);
                high += Vector128.Create(pixel[2..]);
            }

            low.CopyTo(sum);
            high.CopyTo(sum[2..]);
            return;
        }

        if (channels == 3 && Vector128.IsHardwareAccelerated)
        {
            var colors = Vector128.Create(sum);
            var last = sum[2];
            for (var x = 0; x < pixels.Length; x += 3)
            {
                var pixel = pixels.Slice(x, 3);
                colors += Vector128.Create(pixel);
                last += pixel[2];
            }

            colors.CopyTo(sum);
            sum[2] = last;
            return;
        }

        for (var x = 0; x < pixels.Length; x += channels)
        {
            for (var c = 0; c < channels; c++)
            {
                sum[c] += pixels[x + c];
            }
        }
    }

    /// <summary>Gets the number of doubles of the scratch of <see cref="OilPaint{TPixel}"/>: the ring of working rows, the output row and the sums of the levels.</summary>
    public static long GetOilPaintScratchLength(PixelFormat format, int width, int radius, int levels)
    {
        var channels = PixelFormats.GetComponentCount(format);
        return ((((2L * radius) + 2) * width) + ((long)OilPaintSets * levels)) * channels;
    }

    /// <summary>Gets the number of integers of the scratch of <see cref="OilPaint{TPixel}"/>: the levels of the ring of rows and the counts of every level.</summary>
    public static long GetOilPaintLevelsLength(int width, int radius, int levels) => (((2L * radius) + 1) * width) + ((OilPaintSets + 1L) * levels);

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
    public static void OilPaint<TPixel>(scoped in PixelLease lease, int levels, int radius, bool grayColors, EffectScratch scratch, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var format = PixelFormats.GetPixelFormat<TPixel>();
        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            OilPaintCore<ushort>(storage, format, levels, radius, blueFirst: false, grayColors, scratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            OilPaintCore<byte>(storage, format, levels, radius, blueFirst: typeof(TPixel) == typeof(Bgra32), grayColors, scratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaFloat))
        {
            OilPaintCore<float>(storage, format, levels, radius, blueFirst: false, grayColors, scratch, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            OilPaintCore<Half>(storage, format, levels, radius, blueFirst: false, grayColors, scratch, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    private static void OilPaintCore<TSample>(PixelStorage storage, PixelFormat format, int levels, int radius, bool blueFirst, bool grayColors, EffectScratch scratch, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var workers = scratch.Workers;
        if (workers is 1)
        {
            OilPaintBand<TSample>(storage, format, levels, radius, blueFirst, grayColors, scratch.GetDoubles(0), scratch.GetInts(0), halo: default, 0, storage.Height, cancellationToken);
            return;
        }

        // Every band reads the rows within the radius of its edges as they were: copy them before any row is rewritten
        var rowLength = storage.RowLength;
        for (var band = 0; band < workers; band++)
        {
            var halo = scratch.GetHalo(band);
            var start = scratch.GetBandStart(band);
            var end = scratch.GetBandStart(band + 1);
            for (var y = Math.Max(0, start - radius); y < start; y++)
            {
                storage.GetRowSpanCore(y).CopyTo(halo.Slice((y - start + radius) * rowLength, rowLength));
            }

            for (var y = end; y < Math.Min(storage.Height, end + radius); y++)
            {
                storage.GetRowSpanCore(y).CopyTo(halo.Slice((radius + y - end) * rowLength, rowLength));
            }
        }

        RowBands.RunParallel(workers, band => OilPaintBand<TSample>(storage, format, levels, radius, blueFirst, grayColors, scratch.GetDoubles(band), scratch.GetInts(band), scratch.GetHalo(band), scratch.GetBandStart(band), scratch.GetBandStart(band + 1), cancellationToken));
    }

    /// <summary>
    /// Paints the rows <c>[start, end)</c>. The rows of the window outside the band are read from <paramref name="halo"/>
    /// (the radius above the band, then the radius below it), the others from the storage, each one before it is rewritten.
    /// </summary>
    private static void OilPaintBand<TSample>(PixelStorage storage, PixelFormat format, int levels, int radius, bool blueFirst, bool grayColors, Span<double> scratch, Span<int> levelScratch, ReadOnlySpan<byte> halo, int start, int end, CancellationToken cancellationToken)
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
        var sliding = !WorkingSamples.IsFloatingPoint<TSample>();

        // The floating-point sums are made in the order of the pixels, in one set
        var sets = sliding ? OilPaintSets : 1;
        var sums = scratch.Slice((window.Diameter + 1) * rowLength, sets * levels * window.Channels);
        var ringLevels = levelScratch[..(window.Diameter * width)];
        var counts = levelScratch.Slice(window.Diameter * width, sets * levels);
        var total = levelScratch.Slice((window.Diameter * width) + (sets * levels), levels);

        // The start in the ring of each row of the window of the current row, and its set
        Span<int> rowStarts = window.Diameter <= 256 ? stackalloc int[window.Diameter] : new int[window.Diameter];
        Span<int> rowSets = window.Diameter <= 256 ? stackalloc int[window.Diameter] : new int[window.Diameter];

        // The rows of the first window. Every row is read before it is written: the window of a row reaches the rows
        // below it, and the slot of a row in the ring is reused once no window reads it
        for (var y = Math.Max(0, start - radius); y < Math.Min(start + radius, height); y++)
        {
            LoadOilPaintRow(GetOilPaintRow<TSample>(storage, halo, radius, start, end, y), y, window, ring, ringLevels);
        }

        for (var y = start; y < end; y++)
        {
            if ((y - start) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (y + radius < height)
            {
                LoadOilPaintRow(GetOilPaintRow<TSample>(storage, halo, radius, start, end, y + radius), y + radius, window, ring, ringLevels);
            }

            var top = Math.Max(0, y - radius);
            var bottom = Math.Min(height - 1, y + radius);
            var starts = rowStarts[..(bottom - top + 1)];
            var rowSet = rowSets[..starts.Length];
            for (var v = top; v <= bottom; v++)
            {
                starts[v - top] = v % window.Diameter * width;
                rowSet[v - top] = v % sets;
            }

            for (var x = 0; x < width; x++)
            {
                if (!sliding || x == 0)
                {
                    sums.Clear();
                    counts.Clear();
                    for (var column = Math.Max(0, x - radius); column <= Math.Min(width - 1, x + radius); column++)
                    {
                        AddOilPaintColumn(column, starts, rowSet, window, ring, ringLevels, sums, counts);
                    }
                }
                else if (x - radius - 1 >= 0 && x + radius < width)
                {
                    SlideOilPaintWindow(x - radius - 1, x + radius, starts, rowSet, window, ring, ringLevels, sums, counts);
                }
                else
                {
                    if (x - radius - 1 >= 0)
                    {
                        RemoveOilPaintColumn(x - radius - 1, starts, rowSet, window, ring, ringLevels, sums, counts);
                    }

                    if (x + radius < width)
                    {
                        AddOilPaintColumn(x + radius, starts, rowSet, window, ring, ringLevels, sums, counts);
                    }
                }

                ReadOnlySpan<int> levelCounts = counts;
                if (sets > 1)
                {
                    AddSets(counts, total);
                    levelCounts = total;
                }

                var level = GetMostFrequentLevel(levelCounts);
                var sum = sums.Slice(level * window.Channels, window.Channels);
                var pixel = output.Slice(x * window.Channels, window.Channels);
                double count = levelCounts[level];
                if (window.Channels == 4 && Vector128.IsHardwareAccelerated)
                {
                    // The sets hold integers: their sum is exact
                    var divisor = Vector128.Create(count);
                    var low = Vector128.Create(sum);
                    var high = Vector128.Create(sum[2..]);
                    if (sets > 1)
                    {
                        var other = sums.Slice((levels + level) * 4, 4);
                        low += Vector128.Create(other);
                        high += Vector128.Create(other[2..]);
                    }

                    (low / divisor).CopyTo(pixel);
                    (high / divisor).CopyTo(pixel[2..]);
                }
                else
                {
                    var other = sets > 1 ? sums.Slice((levels + level) * window.Channels, window.Channels) : default;
                    for (var c = 0; c < window.Channels; c++)
                    {
                        pixel[c] = (sets > 1 ? sum[c] + other[c] : sum[c]) / count;
                    }
                }
            }

            WorkingSamples.Store<TSample>(output, GetSamples<TSample>(storage, y), window.Alpha, transfer: 0, max);
        }
    }

    /// <summary>Gets a row read by the band <c>[start, end)</c>: from the storage inside it, from its halos outside it.</summary>
    private static ReadOnlySpan<TSample> GetOilPaintRow<TSample>(PixelStorage storage, ReadOnlySpan<byte> halo, int radius, int start, int end, int y)
        where TSample : unmanaged
    {
        if (y >= start && y < end)
            return GetSamples<TSample>(storage, y);

        var rowLength = storage.RowLength;
        var slot = y < start ? y - start + radius : radius + y - end;
        return unsafe(MemoryMarshal.Cast<byte, TSample>(halo.Slice(slot * rowLength, rowLength)));
    }

    /// <summary>Adds up the counts of every level of the <see cref="OilPaintSets"/> sets.</summary>
    private static void AddSets(ReadOnlySpan<int> counts, Span<int> total)
    {
        var levels = total.Length;
        var first = counts[..levels];
        var second = counts.Slice(levels, levels);
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; i + Vector128<int>.Count <= levels; i += Vector128<int>.Count)
            {
                (Vector128.Create(first[i..]) + Vector128.Create(second[i..])).CopyTo(total[i..]);
            }
        }

        for (; i < levels; i++)
        {
            total[i] = first[i] + second[i];
        }
    }

    /// <summary>Gets the most frequent level, the lowest one on a tie.</summary>
    internal static int GetMostFrequentLevel(ReadOnlySpan<int> counts)
    {
        // The largest count, then its first level: two passes without a branch per level
        int largest;
        if (Vector128.IsHardwareAccelerated && counts.Length >= Vector128<int>.Count && !Resampler.TestForceScalar)
        {
            var maximum = Vector128.Create(counts);
            for (var i = Vector128<int>.Count; i < counts.Length; i += Vector128<int>.Count)
            {
                // The last vector overlaps the one before it
                maximum = Vector128.Max(maximum, Vector128.Create(counts[Math.Min(i, counts.Length - Vector128<int>.Count)..]));
            }

            largest = Math.Max(Math.Max(maximum.GetElement(0), maximum.GetElement(1)), Math.Max(maximum.GetElement(2), maximum.GetElement(3)));
        }
        else
        {
            largest = 0;
            foreach (var count in counts)
            {
                largest = Math.Max(largest, count);
            }
        }

        return counts.IndexOf(largest);
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

    /// <summary>Adds the pixels of a column of the window to the counts and the sums of their levels.</summary>
    /// <param name="column">The column.</param>
    /// <param name="rowStarts">The start of each row of the window in the ring, in pixels.</param>
    /// <param name="rowSets">The set of the sums and the counts of each row of the window.</param>
    /// <param name="window">The layout of the window.</param>
    /// <param name="ring">The working samples of the rows of the ring.</param>
    /// <param name="ringLevels">The levels of the pixels of the ring.</param>
    /// <param name="sums">The sums of the working samples of each level, set after set.</param>
    /// <param name="counts">The number of pixels of each level, set after set.</param>
    private static void AddOilPaintColumn(int column, ReadOnlySpan<int> rowStarts, ReadOnlySpan<int> rowSets, in OilPaintWindow window, ReadOnlySpan<double> ring, ReadOnlySpan<int> ringLevels, Span<double> sums, Span<int> counts)
    {
        var channels = window.Channels;
        var levels = window.Levels;
        for (var row = 0; row < rowStarts.Length; row++)
        {
            var index = rowStarts[row] + column;
            var level = (rowSets[row] * levels) + ringLevels[index];
            counts[level]++;
            var pixel = ring.Slice(index * channels, channels);
            var sum = sums.Slice(level * channels, channels);
            if (channels == 4 && Vector128.IsHardwareAccelerated)
            {
                (Vector128.Create(sum) + Vector128.Create(pixel)).CopyTo(sum);
                (Vector128.Create(sum[2..]) + Vector128.Create(pixel[2..])).CopyTo(sum[2..]);
            }
            else
            {
                for (var c = 0; c < channels; c++)
                {
                    sum[c] += pixel[c];
                }
            }
        }
    }

    /// <summary>
    /// Removes the pixels of a column of the window and adds those of another one, row by row: a row whose two pixels have
    /// the same level changes its sums once, and not its count. Only for the integer formats, whose sums are exact in any
    /// order.
    /// </summary>
    private static void SlideOilPaintWindow(int leaving, int entering, ReadOnlySpan<int> rowStarts, ReadOnlySpan<int> rowSets, in OilPaintWindow window, ReadOnlySpan<double> ring, ReadOnlySpan<int> ringLevels, Span<double> sums, Span<int> counts)
    {
        var channels = window.Channels;
        if (channels != 4 || !Vector128.IsHardwareAccelerated)
        {
            RemoveOilPaintColumn(leaving, rowStarts, rowSets, window, ring, ringLevels, sums, counts);
            AddOilPaintColumn(entering, rowStarts, rowSets, window, ring, ringLevels, sums, counts);
            return;
        }

        var levels = window.Levels;
        for (var row = 0; row < rowStarts.Length; row++)
        {
            var start = rowStarts[row];
            var set = rowSets[row] * levels;
            var leavingLevel = set + ringLevels[start + leaving];
            var enteringLevel = set + ringLevels[start + entering];
            var leavingPixel = ring.Slice(4 * (start + leaving), 4);
            var enteringPixel = ring.Slice(4 * (start + entering), 4);
            var leavingLow = Vector128.Create(leavingPixel);
            var leavingHigh = Vector128.Create(leavingPixel[2..]);
            var enteringLow = Vector128.Create(enteringPixel);
            var enteringHigh = Vector128.Create(enteringPixel[2..]);
            counts[leavingLevel]--;
            counts[enteringLevel]++;
            var removed = sums.Slice(4 * leavingLevel, 4);
            (Vector128.Create(removed) - leavingLow).CopyTo(removed);
            (Vector128.Create(removed[2..]) - leavingHigh).CopyTo(removed[2..]);
            var added = sums.Slice(4 * enteringLevel, 4);
            (Vector128.Create(added) + enteringLow).CopyTo(added);
            (Vector128.Create(added[2..]) + enteringHigh).CopyTo(added[2..]);
        }
    }

    /// <summary>Removes the pixels of a column of the window from the counts and the sums of their levels (see <see cref="AddOilPaintColumn"/>).</summary>
    private static void RemoveOilPaintColumn(int column, ReadOnlySpan<int> rowStarts, ReadOnlySpan<int> rowSets, in OilPaintWindow window, ReadOnlySpan<double> ring, ReadOnlySpan<int> ringLevels, Span<double> sums, Span<int> counts)
    {
        var channels = window.Channels;
        var levels = window.Levels;
        for (var row = 0; row < rowStarts.Length; row++)
        {
            var index = rowStarts[row] + column;
            var level = (rowSets[row] * levels) + ringLevels[index];
            counts[level]--;
            var pixel = ring.Slice(index * channels, channels);
            var sum = sums.Slice(level * channels, channels);
            if (channels == 4 && Vector128.IsHardwareAccelerated)
            {
                (Vector128.Create(sum) - Vector128.Create(pixel)).CopyTo(sum);
                (Vector128.Create(sum[2..]) - Vector128.Create(pixel[2..])).CopyTo(sum[2..]);
            }
            else
            {
                for (var c = 0; c < channels; c++)
                {
                    sum[c] -= pixel[c];
                }
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
    public static void Flatten<TPixel>(scoped in PixelLease lease, Rgba64 background, int maxDegreeOfParallelism, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (typeof(TPixel) != typeof(Rgba32) && typeof(TPixel) != typeof(Bgra32) && typeof(TPixel) != typeof(Rgba64) && typeof(TPixel) != typeof(RgbaFloat) && typeof(TPixel) != typeof(RgbaHalf))
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");

        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        RowBands.For(new Size(storage.Width, storage.Height), maxDegreeOfParallelism, (start, end) => FlattenRows<TPixel>(storage, background, start, end, cancellationToken));
    }

    private static void FlattenRows<TPixel>(PixelStorage storage, Rgba64 background, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        Span<Rgb24> opaque = stackalloc Rgb24[64];
        var (bgR, bgG, bgB) = (background.R / (double)ushort.MaxValue, background.G / (double)ushort.MaxValue, background.B / (double)ushort.MaxValue);
        for (var y = start; y < end; y++)
        {
            if ((y - start) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = GetSamples<TPixel>(storage, y);
            if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
            {
                // The conversion to Rgb24 flattens the 8-bit samples, and the conversion back adds an opaque alpha
                for (var x = 0; x < row.Length; x += opaque.Length)
                {
                    var pixels = row.Slice(x, Math.Min(opaque.Length, row.Length - x));
                    var colors = opaque[..pixels.Length];
                    PixelConverter.ConvertRow<TPixel, Rgb24>(pixels, colors, background);
                    PixelConverter.ConvertRow<Rgb24, TPixel>(colors, pixels);
                }
            }
            else if (typeof(TPixel) == typeof(Rgba64))
            {
                var pixels = unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(row));
                var done = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar ? Flatten64Vector(pixels, background) : 0;
                foreach (ref var pixel in pixels[done..])
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

    /// <summary>
    /// Flattens the first <see cref="Rgba64"/> pixels of a row with vectors, with the result of
    /// <see cref="PixelConverter.Flatten"/> (an opaque pixel is unchanged); returns the number of pixels done.
    /// </summary>
    /// <remarks>
    /// <c>x = s a + bg (65535 - a) + 32767</c> is at most 4,294,868,992, and <c>floor(x / 65535) = (x + (x &gt;&gt; 16) + 1) &gt;&gt; 16</c>
    /// for every such <c>x</c> (checked exhaustively), without overflow.
    /// </remarks>
    internal static int Flatten64Vector(Span<Rgba64> pixels, Rgba64 background)
    {
        var samples = unsafe(MemoryMarshal.Cast<Rgba64, ushort>(pixels));
        var backgroundColor = Vector128.Create(background.R, background.G, background.B, 0u);
        var i = 0;
        for (; i + 2 <= pixels.Length; i += 2)
        {
            var (first, second) = Vector128.Widen(Vector128.Create(samples.Slice(4 * i, 8)));
            Vector128.Narrow(Flatten64(first, backgroundColor), Flatten64(second, backgroundColor)).CopyTo(samples.Slice(4 * i, 8));
        }

        return i;
    }

    /// <summary>Flattens one pixel of 16-bit samples in 32-bit lanes (see <see cref="Flatten64Vector"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> Flatten64(Vector128<uint> pixel, Vector128<uint> background)
    {
        var max = Vector128.Create((uint)ushort.MaxValue);
        var alpha = Vector128.Shuffle(pixel, Vector128.Create(3u, 3, 3, 3));
        var x = (pixel * alpha) + (background * (max - alpha)) + Vector128.Create((uint)(ushort.MaxValue / 2));
        var quotient = Vector128.ShiftRightLogical(x + Vector128.ShiftRightLogical(x, 16) + Vector128<uint>.One, 16);

        // The alpha becomes opaque
        return quotient | Vector128.Create(0, 0, 0, (uint)ushort.MaxValue);
    }

    private static Span<TSample> GetSamples<TSample>(PixelStorage storage, int y)
        where TSample : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(y)));
}
