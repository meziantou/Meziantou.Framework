using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The in-place flat morphological filters: every sample becomes the minimum (erosion) or the maximum (dilation) of the
/// same sample of the pixels of the structuring element around it, and the operations made of them (opening, closing,
/// top-hat transforms). Nothing is computed or rounded: the result of an erosion or a dilation is one of the samples.
/// </summary>
/// <remarks>
/// <para>
/// Samples are compared through the keys of the median filter (<see cref="MedianFilter.ToKey"/>), whose unsigned order is
/// the order of the samples: integer samples are their own keys, and floating-point samples are ordered as numbers, -0
/// below +0, with every number that is not a number above the positive infinity. The keys are 8, 16 or 32 bits wide.
/// </para>
/// <para>
/// A rectangle is filtered in two passes with the algorithm of van Herk (1992) and Gil and Werman (1993): the samples of
/// a row are split into blocks as long as the window, the running extrema of every block are computed forward and
/// backward, and every window, which spans at most two blocks, is the extremum of a backward value and a forward value:
/// three comparisons per sample, whatever the size of the window. The rows are filtered first, then the columns, by
/// blocks of filtered rows. An ellipse is the union of one row segment per row of its height: every output row is the
/// extremum of the rows around it, each filtered with the half-width of its segment in the same way, so the time per
/// sample grows with the height of the ellipse, not with its area.
/// </para>
/// <para>
/// The frame is rewritten in place: the rows a pass reads after rewriting others are copied first (the blocks, the ring
/// of an ellipse, and the halos above and below the frame, loaded before any row changes), so every window reads original
/// samples. Cancellation is checked between rows.
/// </para>
/// </remarks>
internal static class MorphologyFilter
{
    /// <summary>Applies the operation of the plan to a leased frame of the pixel format of the plan, in place.</summary>
    public static void Apply(scoped in PixelLease lease, MorphologyPlan plan, CancellationToken cancellationToken)
    {
        var storage = lease.Storage;
        if (storage.Width != plan.Size.Width || storage.Height != plan.Size.Height || storage.BytesPerPixel != PixelFormats.GetBytesPerPixel(plan.Format))
            throw new InvalidOperationException("The storage does not have the layout the filter was prepared for.");

        switch (plan.Format)
        {
            case PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Gray8:
                Apply<byte, byte>(storage, plan, cancellationToken);
                break;
            case PixelFormat.Rgba64 or PixelFormat.Gray16 or PixelFormat.Rgb48:
                Apply<ushort, ushort>(storage, plan, cancellationToken);
                break;
            case PixelFormat.RgbaHalf:
                Apply<Half, ushort>(storage, plan, cancellationToken);
                break;
            case PixelFormat.RgbaFloat:
                Apply<float, uint>(storage, plan, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"The pixel format '{plan.Format}' is not supported.");
        }
    }

    private static void Apply<TSample, TKey>(PixelStorage storage, MorphologyPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = plan.Operation;
        if (operation is MorphologyOperation.TopHat or MorphologyOperation.BlackHat)
        {
            var original = plan.Original;
            var length = storage.Width * storage.BytesPerPixel;
            for (var y = 0; y < storage.Height; y++)
            {
                storage.GetRowSpanCore(y)[..length].CopyTo(original.GetRowSpanCore(y));
            }
        }

        switch (operation)
        {
            case MorphologyOperation.Erode:
                Filter<TSample, TKey, Minimum<TKey>>(storage, plan, cancellationToken);
                break;
            case MorphologyOperation.Dilate:
                Filter<TSample, TKey, Maximum<TKey>>(storage, plan, cancellationToken);
                break;
            case MorphologyOperation.Open or MorphologyOperation.TopHat:
                Filter<TSample, TKey, Minimum<TKey>>(storage, plan, cancellationToken);
                Filter<TSample, TKey, Maximum<TKey>>(storage, plan, cancellationToken);
                break;
            default:
                Filter<TSample, TKey, Maximum<TKey>>(storage, plan, cancellationToken);
                Filter<TSample, TKey, Minimum<TKey>>(storage, plan, cancellationToken);
                break;
        }

        if (operation == MorphologyOperation.TopHat)
        {
            Subtract<TSample>(plan.Original, storage, storage, plan, cancellationToken);
        }
        else if (operation == MorphologyOperation.BlackHat)
        {
            Subtract<TSample>(storage, plan.Original, storage, plan, cancellationToken);
        }
    }

    /// <summary>
    /// Stores <c>minuend - subtrahend</c> in <paramref name="destination"/> for the color samples (clamped to 0 for integer
    /// samples; floating-point differences are rounded to the sample type) and keeps the alpha of the original frame
    /// (<see cref="MorphologyPlan.Original"/>).
    /// </summary>
    private static void Subtract<TSample>(PixelStorage minuend, PixelStorage subtrahend, PixelStorage destination, MorphologyPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var channels = plan.Channels;
        var length = plan.RowLength;
        var hasAlpha = channels == 4;
        for (var y = 0; y < destination.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var a = unsafe(MemoryMarshal.Cast<byte, TSample>(minuend.GetRowSpanCore(y)))[..length];
            var b = unsafe(MemoryMarshal.Cast<byte, TSample>(subtrahend.GetRowSpanCore(y)))[..length];
            var original = unsafe(MemoryMarshal.Cast<byte, TSample>(plan.Original.GetRowSpanCore(y)))[..length];
            var result = unsafe(MemoryMarshal.Cast<byte, TSample>(destination.GetRowSpanCore(y)))[..length];
            for (var i = 0; i < length; i++)
            {
                result[i] = hasAlpha && i % 4 == 3 ? original[i] : Difference(a[i], b[i]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TSample Difference<TSample>(TSample a, TSample b)
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(byte))
        {
            var value = (byte)Math.Max(0, unsafe(Unsafe.As<TSample, byte>(ref a)) - unsafe(Unsafe.As<TSample, byte>(ref b)));
            return unsafe(Unsafe.As<byte, TSample>(ref value));
        }

        if (typeof(TSample) == typeof(ushort))
        {
            var value = (ushort)Math.Max(0, unsafe(Unsafe.As<TSample, ushort>(ref a)) - unsafe(Unsafe.As<TSample, ushort>(ref b)));
            return unsafe(Unsafe.As<ushort, TSample>(ref value));
        }

        if (typeof(TSample) == typeof(Half))
        {
            var value = (Half)((float)unsafe(Unsafe.As<TSample, Half>(ref a)) - (float)unsafe(Unsafe.As<TSample, Half>(ref b)));
            return unsafe(Unsafe.As<Half, TSample>(ref value));
        }

        var difference = unsafe(Unsafe.As<TSample, float>(ref a)) - unsafe(Unsafe.As<TSample, float>(ref b));
        return unsafe(Unsafe.As<float, TSample>(ref difference));
    }

    private static void Filter<TSample, TKey, TOperator>(PixelStorage storage, MorphologyPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        if (plan.Element.IsRectangle)
        {
            FilterRectangle<TSample, TKey, TOperator>(storage, plan, cancellationToken);
        }
        else
        {
            FilterEllipse<TSample, TKey, TOperator>(storage, plan, cancellationToken);
        }
    }

    /// <summary>
    /// Filters by a rectangle: each row by its width (<see cref="FilterRow"/>), then the columns of the filtered rows, by
    /// blocks of <c>k = 2 RadiusY + 1</c> virtual rows (the frame with <c>RadiusY</c> halo rows above and below). The output
    /// row <c>y</c> is the extremum of the virtual rows <c>y</c> to <c>y + 2 RadiusY</c>: the backward extremum of row
    /// <c>y</c> in its block, and the forward extremum of row <c>y + 2 RadiusY</c> in the next block. The rows of a block are
    /// written once the next block is loaded, which never reads a row already written.
    /// </summary>
    private static void FilterRectangle<TSample, TKey, TOperator>(PixelStorage storage, MorphologyPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        var height = plan.Size.Height;
        var radiusY = plan.Element.RadiusY;
        var k = plan.Element.Height;
        var row = plan.RowLength;
        var padded = plan.PaddedLength;
        var keys = plan.GetKeys<TKey>();
        var halos = keys[..(2 * radiusY * row)];
        var backward = keys.Slice(halos.Length, k * row);
        var raw = keys.Slice(halos.Length + (k * row), k * row);
        var forward = keys.Slice(halos.Length + (2 * k * row), k * row);
        var scratch = keys.Slice(halos.Length + (3 * k * row), 3 * padded);
        var virtualRows = height + (2 * radiusY);

        // The halos read original rows: filter them all before the first row is rewritten
        for (var i = 0; i < radiusY; i++)
        {
            LoadFilteredRow<TSample, TKey, TOperator>(storage, plan, i - radiusY, scratch, halos.Slice(i * row, row));
            LoadFilteredRow<TSample, TKey, TOperator>(storage, plan, height + i, scratch, halos.Slice((radiusY + i) * row, row));
        }

        // Block 0, then each next block before the rows of the current one are written
        LoadBlock<TSample, TKey, TOperator>(storage, plan, 0, halos, raw, scratch);
        Backward<TKey, TOperator>(raw, backward, Math.Min(k, virtualRows), row);
        for (var start = 0; start < height; start += k)
        {
            var next = start + k;
            var nextCount = Math.Min(k, virtualRows - next);
            if (nextCount > 0)
            {
                LoadBlock<TSample, TKey, TOperator>(storage, plan, next, halos, raw, scratch);
                for (var i = 0; i < nextCount; i++)
                {
                    var destination = forward.Slice(i * row, row);
                    if (i == 0)
                    {
                        raw[..row].CopyTo(destination);
                    }
                    else
                    {
                        Combine<TKey, TOperator>(forward.Slice((i - 1) * row, row), raw.Slice(i * row, row), destination);
                    }
                }
            }

            for (var y = start; y < Math.Min(next, height); y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var output = scratch[..row];
                var last = y + k - 1;
                if (last < next)
                {
                    backward.Slice((y - start) * row, row).CopyTo(output);
                }
                else
                {
                    Combine<TKey, TOperator>(backward.Slice((y - start) * row, row), forward.Slice((last - next) * row, row), output);
                }

                StoreRow<TSample, TKey>(storage, y, output);
            }

            if (nextCount > 0)
            {
                Backward<TKey, TOperator>(raw, backward, nextCount, row);
            }
        }
    }

    /// <summary>Loads the filtered virtual rows of the block that starts at virtual row <paramref name="first"/> (image row <c>first - RadiusY</c>) into <paramref name="raw"/>.</summary>
    private static void LoadBlock<TSample, TKey, TOperator>(PixelStorage storage, MorphologyPlan plan, int first, ReadOnlySpan<TKey> halos, Span<TKey> raw, Span<TKey> scratch)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        var height = plan.Size.Height;
        var radiusY = plan.Element.RadiusY;
        var row = plan.RowLength;
        var count = Math.Min(plan.Element.Height, height + (2 * radiusY) - first);
        for (var i = 0; i < count; i++)
        {
            var source = first + i - radiusY;
            var destination = raw.Slice(i * row, row);
            if (source < 0)
            {
                halos.Slice((source + radiusY) * row, row).CopyTo(destination);
            }
            else if (source >= height)
            {
                halos.Slice((radiusY + source - height) * row, row).CopyTo(destination);
            }
            else
            {
                LoadFilteredRow<TSample, TKey, TOperator>(storage, plan, source, scratch, destination);
            }
        }
    }

    /// <summary>Computes the backward extrema of the first <paramref name="count"/> rows of a block, from its last row.</summary>
    private static void Backward<TKey, TOperator>(ReadOnlySpan<TKey> rows, Span<TKey> destination, int count, int row)
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        rows.Slice((count - 1) * row, row).CopyTo(destination.Slice((count - 1) * row, row));
        for (var i = count - 2; i >= 0; i--)
        {
            Combine<TKey, TOperator>(destination.Slice((i + 1) * row, row), rows.Slice(i * row, row), destination.Slice(i * row, row));
        }
    }

    /// <summary>
    /// Filters by an ellipse: every output row is the extremum, over the rows of the ellipse, of the row at that offset
    /// filtered by the half-width of its segment. The padded original rows are kept in a ring as tall as the ellipse, and
    /// the rows above and below the frame in the halos, loaded before any row is rewritten.
    /// </summary>
    private static void FilterEllipse<TSample, TKey, TOperator>(PixelStorage storage, MorphologyPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        var height = plan.Size.Height;
        var element = plan.Element;
        var radiusY = element.RadiusY;
        var k = element.Height;
        var row = plan.RowLength;
        var padded = plan.PaddedLength;
        var keys = plan.GetKeys<TKey>();
        var ring = keys[..(k * padded)];
        var halos = keys.Slice(ring.Length, 2 * radiusY * padded);
        var forward = keys.Slice(ring.Length + halos.Length, padded);
        var backward = keys.Slice(ring.Length + halos.Length + padded, padded);
        var output = keys.Slice(ring.Length + halos.Length + (2 * padded), row);

        for (var i = 0; i < radiusY; i++)
        {
            LoadPaddedRow<TSample, TKey>(storage, plan, i - radiusY, halos.Slice(i * padded, padded));
            LoadPaddedRow<TSample, TKey>(storage, plan, height + i, halos.Slice((radiusY + i) * padded, padded));
        }

        // The rows of the frame the first output row reads below it
        for (var source = 0; source < Math.Min(radiusY, height); source++)
        {
            LoadPaddedRow<TSample, TKey>(storage, plan, source, ring.Slice((source % k) * padded, padded));
        }

        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (y + radiusY < height)
            {
                LoadPaddedRow<TSample, TKey>(storage, plan, y + radiusY, ring.Slice(((y + radiusY) % k) * padded, padded));
            }

            for (var dy = -radiusY; dy <= radiusY; dy++)
            {
                var source = y + dy;
                var keysOfRow = source < 0 ? halos.Slice((source + radiusY) * padded, padded)
                    : source >= height ? halos.Slice((radiusY + source - height) * padded, padded)
                    : ring.Slice((source % k) * padded, padded);
                FilterRow<TKey, TOperator>(keysOfRow, element.RadiusX, element.GetHalfWidth(dy), plan.Channels, forward, backward, output, combine: dy != -radiusY);
            }

            StoreRow<TSample, TKey>(storage, y, output);
        }
    }

    /// <summary>Loads row <paramref name="y"/> (mapped by the edge rule), padded, and filters it by the width of the rectangle into <paramref name="destination"/>.</summary>
    private static void LoadFilteredRow<TSample, TKey, TOperator>(PixelStorage storage, MorphologyPlan plan, int y, Span<TKey> scratch, Span<TKey> destination)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        var padded = plan.PaddedLength;
        var row = scratch[..padded];
        LoadPaddedRow<TSample, TKey>(storage, plan, y, row);
        FilterRow<TKey, TOperator>(row, plan.Element.RadiusX, plan.Element.RadiusX, plan.Channels, scratch.Slice(padded, padded), scratch.Slice(2 * padded, padded), destination, combine: false);
    }

    /// <summary>
    /// Filters a padded row (<paramref name="padding"/> pixels on each side) by a window of <c>2 halfWidth + 1</c> pixels,
    /// with the running extrema of van Herk and Gil and Werman, and stores the result (or its extremum with what
    /// <paramref name="output"/> holds when <paramref name="combine"/> is set).
    /// </summary>
    internal static void FilterRow<TKey, TOperator>(ReadOnlySpan<TKey> padded, int padding, int halfWidth, int channels, Span<TKey> forward, Span<TKey> backward, Span<TKey> output, bool combine)
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        var row = output.Length;
        var source = padded.Slice((padding - halfWidth) * channels, row + (2 * halfWidth * channels));
        if (halfWidth == 0)
        {
            if (combine)
            {
                Combine<TKey, TOperator>(output, source, output);
            }
            else
            {
                source.CopyTo(output);
            }

            return;
        }

        var window = (2 * halfWidth) + 1;
        var pixels = source.Length / channels;
        var f = forward[..source.Length];
        var b = backward[..source.Length];
        for (var p = 0; p < pixels; p++)
        {
            var offset = p * channels;
            if (p % window == 0)
            {
                source.Slice(offset, channels).CopyTo(f.Slice(offset, channels));
            }
            else
            {
                for (var c = 0; c < channels; c++)
                {
                    f[offset + c] = TOperator.Combine(f[offset - channels + c], source[offset + c]);
                }
            }
        }

        for (var p = pixels - 1; p >= 0; p--)
        {
            var offset = p * channels;
            if (p == pixels - 1 || (p + 1) % window == 0)
            {
                source.Slice(offset, channels).CopyTo(b.Slice(offset, channels));
            }
            else
            {
                for (var c = 0; c < channels; c++)
                {
                    b[offset + c] = TOperator.Combine(b[offset + channels + c], source[offset + c]);
                }
            }
        }

        // The window of output pixel x covers the source pixels x to x + window - 1
        var last = (window - 1) * channels;
        if (combine)
        {
            for (var i = 0; i < row; i++)
            {
                output[i] = TOperator.Combine(output[i], TOperator.Combine(b[i], f[i + last]));
            }
        }
        else
        {
            for (var i = 0; i < row; i++)
            {
                output[i] = TOperator.Combine(b[i], f[i + last]);
            }
        }
    }

    private static void Combine<TKey, TOperator>(ReadOnlySpan<TKey> a, ReadOnlySpan<TKey> b, Span<TKey> destination)
        where TKey : unmanaged, IBinaryInteger<TKey>
        where TOperator : struct, IExtremum<TKey>
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = TOperator.Combine(a[i], b[i]);
        }
    }

    /// <summary>Loads the keys of row <paramref name="y"/>, which may be outside the frame, padded by the horizontal radius on each side with the pixels the edge rule reads.</summary>
    private static void LoadPaddedRow<TSample, TKey>(PixelStorage storage, MorphologyPlan plan, int y, Span<TKey> destination)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
    {
        var zero = TKey.CreateTruncating(MedianFilter.ToKey<TSample>(default));
        var source = plan.MapRow(y);
        if (source < 0)
        {
            destination.Fill(zero);
            return;
        }

        var channels = plan.Channels;
        var width = plan.Size.Width;
        var radius = plan.Element.RadiusX;
        var samples = unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(source)))[..(width * channels)];
        var row = destination.Slice(radius * channels, samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            row[i] = TKey.CreateTruncating(MedianFilter.ToKey(samples[i]));
        }

        for (var i = 0; i < radius; i++)
        {
            FillPadding(destination, i, plan.MapColumn(i - radius), radius, channels, zero);
            FillPadding(destination, radius + width + i, plan.MapColumn(width + i), radius, channels, zero);
        }
    }

    private static void FillPadding<TKey>(Span<TKey> padded, int pixel, int sourceColumn, int radius, int channels, TKey zero)
        where TKey : unmanaged
    {
        var destination = padded.Slice(pixel * channels, channels);
        if (sourceColumn < 0)
        {
            destination.Fill(zero);
        }
        else
        {
            padded.Slice((radius + sourceColumn) * channels, channels).CopyTo(destination);
        }
    }

    private static void StoreRow<TSample, TKey>(PixelStorage storage, int y, ReadOnlySpan<TKey> keys)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>
    {
        var samples = unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(y)))[..keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            samples[i] = MedianFilter.FromKey<TSample>(uint.CreateTruncating(keys[i]));
        }
    }

    /// <summary>The extremum of two keys: the minimum for an erosion, the maximum for a dilation.</summary>
    internal interface IExtremum<TKey>
    {
        static abstract TKey Combine(TKey a, TKey b);
    }

    private readonly struct Minimum<TKey> : IExtremum<TKey>
        where TKey : IBinaryInteger<TKey>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TKey Combine(TKey a, TKey b) => TKey.Min(a, b);
    }

    private readonly struct Maximum<TKey> : IExtremum<TKey>
        where TKey : IBinaryInteger<TKey>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TKey Combine(TKey a, TKey b) => TKey.Max(a, b);
    }
}
