using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Scalar row kernels of the basic processing operations: exact geometry permutations (crop,
/// quarter-turn rotations, mirrors, diagonal reflections) and in-place flip and grayscale. The kernels are generic over the
/// pixel struct (typed dispatch, no operation objects) and never resample: every destination pixel is a bit-exact copy of
/// one source pixel, so 16-bit samples and alpha survive unchanged.
/// </summary>
/// <remarks>Cancellation is observed between row bands; the callers decide whether a partial result is visible.</remarks>
internal static class ProcessingKernels
{
    /// <summary>The side of the square tiles used by transposing kernels (at most 8 KiB of stack for 8-byte pixels).</summary>
    internal const int TileSize = 32;

    /// <summary>The number of rows processed between two cancellation checks.</summary>
    internal const int RowsPerCancellationCheck = 64;

    /// <summary>
    /// Writes <paramref name="region"/> of the source, permuted by <paramref name="transform"/>, to the whole destination.
    /// The destination size must be <c>transform.GetOutputSize(region.Size)</c>; the region must be inside the source.
    /// </summary>
    public static void Transform<TPixel>(scoped in PixelLease source, scoped in PixelLease destination, Rectangle region, OrientationTransform transform, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var outputWidth = destination.Width;
        var outputHeight = destination.Height;
        Debug.Assert(new Size(outputWidth, outputHeight) == transform.GetOutputSize(region.Size));
        Debug.Assert(region.X >= 0 && region.Y >= 0 && region.Right <= source.Width && region.Bottom <= source.Height);

        if (!transform.Transpose)
        {
            // Destination row dy is (a slice of) one source row, possibly reversed
            for (var dy = 0; dy < outputHeight; dy++)
            {
                if (dy % RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var sy = region.Y + (transform.FlipY ? region.Height - 1 - dy : dy);
                var output = destination.GetRow<TPixel>(dy);
                source.GetRow<TPixel>(sy).Slice(region.X, region.Width).CopyTo(output);
                if (transform.FlipX)
                {
                    output.Reverse();
                }
            }

            return;
        }

        // Destination column dx comes from source row sy(dx) and destination row dy from source column sx(dy). Go through
        // square tiles so that each source and destination row is fetched once per tile instead of once per pixel.
        Span<TPixel> tile = stackalloc TPixel[TileSize * TileSize];
        for (var tileY = 0; tileY < outputHeight; tileY += TileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tileHeight = Math.Min(TileSize, outputHeight - tileY);
            for (var tileX = 0; tileX < outputWidth; tileX += TileSize)
            {
                var tileWidth = Math.Min(TileSize, outputWidth - tileX);
                for (var i = 0; i < tileWidth; i++)
                {
                    var dx = tileX + i;
                    var sy = region.Y + (transform.FlipY ? region.Height - 1 - dx : dx);
                    var row = source.GetRow<TPixel>(sy);
                    for (var j = 0; j < tileHeight; j++)
                    {
                        var dy = tileY + j;
                        var sx = region.X + (transform.FlipX ? region.Width - 1 - dy : dy);
                        tile[(j * TileSize) + i] = row[sx];
                    }
                }

                for (var j = 0; j < tileHeight; j++)
                {
                    tile.Slice(j * TileSize, tileWidth).CopyTo(destination.GetRow<TPixel>(tileY + j).Slice(tileX, tileWidth));
                }
            }
        }
    }

    /// <summary>
    /// Writes the rectangle of the source whose top-left corner is <paramref name="origin"/> and whose size is the
    /// destination size to the whole destination. The part of the rectangle inside the source is a bit-exact copy; the
    /// part outside the source is <paramref name="fill"/>.
    /// </summary>
    public static void Extend<TPixel>(scoped in PixelLease source, scoped in PixelLease destination, Point origin, TPixel fill, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var outputWidth = destination.Width;
        var outputHeight = destination.Height;

        // The source columns [sourceX, sourceX + count) land at destination column offset
        var sourceX = Math.Max(origin.X, 0);
        var offset = sourceX - origin.X;
        var count = (int)Math.Clamp(Math.Min(source.Width, (long)origin.X + outputWidth) - sourceX, 0, outputWidth);
        if (count == 0)
        {
            offset = 0;
        }

        for (var dy = 0; dy < outputHeight; dy++)
        {
            if (dy % RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var output = destination.GetRow<TPixel>(dy);
            var sy = (long)origin.Y + dy;
            if (count == 0 || sy < 0 || sy >= source.Height)
            {
                output.Fill(fill);
                continue;
            }

            output[..offset].Fill(fill);
            source.GetRow<TPixel>((int)sy).Slice(sourceX, count).CopyTo(output[offset..]);
            output[(offset + count)..].Fill(fill);
        }
    }

    /// <summary>Mirrors the leased storage in place. On cancellation, the rows processed so far stay mirrored.</summary>
    public static void Flip<TPixel>(scoped in PixelLease lease, FlipMode mode, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var height = lease.Height;
        if (mode == FlipMode.Horizontal)
        {
            for (var y = 0; y < height; y++)
            {
                if (y % RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                lease.GetRow<TPixel>(y).Reverse();
            }

            return;
        }

        Debug.Assert(mode == FlipMode.Vertical);
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            if (top % RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            SwapRows(lease.GetRowBytes(top), lease.GetRowBytes(bottom));
        }
    }

    /// <summary>
    /// Replaces the color of every pixel with its Rec. 709 luma on the encoded values, at the storage precision
    /// (<see cref="PixelConverter.Luma(uint, uint, uint, uint)"/>, the normative rule of the library); alpha is
    /// kept. Gray formats are already gray and are left unchanged.
    /// </summary>
    public static void Grayscale<TPixel>(scoped in PixelLease lease, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(Gray8) || typeof(TPixel) == typeof(Gray16))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        var height = lease.Height;
        for (var y = 0; y < height; y++)
        {
            if (y % RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRow<TPixel>(y);
            var done = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar ? GrayscaleVector(row) : 0;
            GrayscaleScalar(row[done..]);
        }
    }

    /// <summary>The scalar reference of the in-place grayscale of one row of a color format.</summary>
    internal static void GrayscaleScalar<TPixel>(Span<TPixel> row)
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(Rgba32))
        {
            foreach (ref var pixel in unsafe(MemoryMarshal.Cast<TPixel, Rgba32>(row)))
            {
                var luma = (byte)PixelConverter.Luma(pixel.R, pixel.G, pixel.B, byte.MaxValue);
                pixel.R = luma;
                pixel.G = luma;
                pixel.B = luma;
            }
        }
        else if (typeof(TPixel) == typeof(Bgra32))
        {
            foreach (ref var pixel in unsafe(MemoryMarshal.Cast<TPixel, Bgra32>(row)))
            {
                var luma = (byte)PixelConverter.Luma(pixel.R, pixel.G, pixel.B, byte.MaxValue);
                pixel.R = luma;
                pixel.G = luma;
                pixel.B = luma;
            }
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            foreach (ref var pixel in unsafe(MemoryMarshal.Cast<TPixel, Rgb24>(row)))
            {
                var luma = (byte)PixelConverter.Luma(pixel.R, pixel.G, pixel.B, byte.MaxValue);
                pixel.R = luma;
                pixel.G = luma;
                pixel.B = luma;
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            foreach (ref var pixel in unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(row)))
            {
                var luma = (ushort)PixelConverter.Luma(pixel.R, pixel.G, pixel.B, ushort.MaxValue);
                pixel.R = luma;
                pixel.G = luma;
                pixel.B = luma;
            }
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>
    /// Replaces the colors of the leading pixels of a row with their luma, 4 pixels at a time
    /// (<see cref="PixelConverter.Luma8Vector"/>, <see cref="PixelConverter.Luma16Vector"/>: the results of
    /// <see cref="PixelConverter.Luma(uint, uint, uint, uint)"/>); alpha is kept.
    /// </summary>
    /// <returns>The number of pixels processed.</returns>
    internal static int GrayscaleVector<TPixel>(Span<TPixel> row)
        where TPixel : unmanaged
    {
        ref var start = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(row)));
        var length = row.Length;
        var i = 0;
        if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
        {
            for (; i + 4 <= length; i += 4)
            {
                var pixels = unsafe(Vector128.LoadUnsafe(ref start, (nuint)(4 * i)));
                var luma = PixelConverter.Luma8Vector(pixels, bgr: typeof(TPixel) == typeof(Bgra32)).AsUInt32();
                var result = (luma * 0x010101u) | (pixels.AsUInt32() & Vector128.Create(0xFF000000u));
                unsafe { result.AsByte().StoreUnsafe(ref start, (nuint)(4 * i)); }
            }
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            // 4 pixels of 3 bytes: 16 bytes are read, only the 12 bytes of the 4 pixels are written
            var expand = Vector128.Create((byte)0, 1, 2, 0xFF, 3, 4, 5, 0xFF, 6, 7, 8, 0xFF, 9, 10, 11, 0xFF);
            var pack = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);
            for (; i + 6 <= length; i += 4)
            {
                var pixels = Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref start, (nuint)(3 * i))), expand);
                var luma = PixelConverter.Luma8Vector(pixels, bgr: false).AsUInt32();
                var result = Vector128.Shuffle((luma * 0x010101u).AsByte(), pack);
                ref var output = ref unsafe(Unsafe.Add(ref start, 3 * i));
                unsafe { Unsafe.WriteUnaligned(ref output, result.AsUInt64().ToScalar()); }
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, 8), result.AsUInt32().GetElement(2)); }
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            var alpha = Vector128.Create(0xFFFF000000000000ul).AsByte();
            var low = Vector128.Create((byte)0, 1, 0, 1, 0, 1, 0xFF, 0xFF, 4, 5, 4, 5, 4, 5, 0xFF, 0xFF);
            var high = Vector128.Create((byte)8, 9, 8, 9, 8, 9, 0xFF, 0xFF, 12, 13, 12, 13, 12, 13, 0xFF, 0xFF);
            for (; i + 4 <= length; i += 4)
            {
                var first = unsafe(Vector128.LoadUnsafe(ref start, (nuint)(8 * i)));
                var second = unsafe(Vector128.LoadUnsafe(ref start, (nuint)((8 * i) + 16)));
                var luma = PixelConverter.Luma16Vector(first.AsUInt16(), second.AsUInt16()).AsByte();
                unsafe { (Vector128.Shuffle(luma, low) | (first & alpha)).StoreUnsafe(ref start, (nuint)(8 * i)); }
                unsafe { (Vector128.Shuffle(luma, high) | (second & alpha)).StoreUnsafe(ref start, (nuint)((8 * i) + 16)); }
            }
        }

        return i;
    }

    private static void SwapRows(Span<byte> first, Span<byte> second)
    {
        Debug.Assert(first.Length == second.Length);
        Span<byte> buffer = stackalloc byte[256];
        for (var offset = 0; offset < first.Length; offset += buffer.Length)
        {
            var length = Math.Min(buffer.Length, first.Length - offset);
            var a = first.Slice(offset, length);
            var b = second.Slice(offset, length);
            var temp = buffer[..length];
            a.CopyTo(temp);
            b.CopyTo(a);
            temp.CopyTo(b);
        }
    }
}
