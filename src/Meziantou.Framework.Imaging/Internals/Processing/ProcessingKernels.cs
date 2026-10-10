using System.Diagnostics;
using System.Runtime.InteropServices;

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
