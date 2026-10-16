using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Scalar row kernels of the basic processing operations: exact geometry permutations (crop,
/// quarter-turn rotations, mirrors, diagonal reflections) and in-place flip and grayscale. The kernels are generic over the
/// pixel struct (typed dispatch, no operation objects) and never resample: every destination pixel is a bit-exact copy of
/// one source pixel, so 16-bit samples and alpha survive unchanged.
/// </summary>
/// <remarks>
/// Cancellation is observed between row bands; the callers decide whether a partial result is visible. With hardware
/// vectors, quarter turns of 4- and 8-byte pixels transpose square blocks in 128-bit registers and grayscale computes 4
/// lumas at a time; both give the results of the scalar loops (<c>VectorizedKernelTests</c>).
/// </remarks>
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
        var transposeBlocks = !Resampler.TestForceScalar && CanTransposeBlocks<TPixel>();
        for (var tileY = 0; tileY < outputHeight; tileY += TileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tileHeight = Math.Min(TileSize, outputHeight - tileY);
            for (var tileX = 0; tileX < outputWidth; tileX += TileSize)
            {
                var tileWidth = Math.Min(TileSize, outputWidth - tileX);

                // Tile row j comes from source column sx(tileY + j) = firstColumn + j * columnStep
                var firstColumn = region.X + (transform.FlipX ? region.Width - 1 - tileY : tileY);
                var columnStep = transform.FlipX ? -1 : 1;
                var i = 0;
                if (transposeBlocks)
                {
                    i = FillTileBlocks(source, tile, region, transform, tileX, tileWidth, tileHeight, firstColumn);
                }

                for (; i < tileWidth; i++)
                {
                    var dx = tileX + i;
                    var sy = region.Y + (transform.FlipY ? region.Height - 1 - dx : dx);
                    var row = source.GetRow<TPixel>(sy);
                    for (var j = 0; j < tileHeight; j++)
                    {
                        tile[(j * TileSize) + i] = row[firstColumn + (j * columnStep)];
                    }
                }

                for (var j = 0; j < tileHeight; j++)
                {
                    tile.Slice(j * TileSize, tileWidth).CopyTo(destination.GetRow<TPixel>(tileY + j).Slice(tileX, tileWidth));
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether <see cref="FillTileBlocks"/> supports the pixel type on this hardware.</summary>
    private static bool CanTransposeBlocks<TPixel>()
        where TPixel : unmanaged
        => BitConverter.IsLittleEndian && (Unsafe.SizeOf<TPixel>() is 4 or 8) && (AdvSimd.Arm64.IsSupported || Sse2.IsSupported);

    /// <summary>
    /// Fills the leading columns of a tile by groups of 4 (4-byte pixels) or 2 (8-byte pixels) source rows: square blocks
    /// of pixels are loaded from the rows, transposed in 128-bit registers and stored to consecutive tile rows (a horizontal
    /// mirror reverses the pixels of each loaded vector). Every tile pixel is the copy the scalar loop makes.
    /// </summary>
    /// <returns>The number of tile columns filled; the rows of the tile beyond the last whole block are filled too.</returns>
    private static int FillTileBlocks<TPixel>(scoped in PixelLease source, Span<TPixel> tile, Rectangle region, OrientationTransform transform, int tileX, int tileWidth, int tileHeight, int firstColumn)
        where TPixel : unmanaged
    {
        var block = 16 / Unsafe.SizeOf<TPixel>();
        var blockRows = tileHeight / block * block;
        ref var tileStart = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(tile)));
        var tileStride = TileSize * Unsafe.SizeOf<TPixel>();
        var i = 0;
        for (; i + block <= tileWidth; i += block)
        {
            // The source rows of tile columns i to i + block - 1
            var dx = tileX + i;
            var sy = region.Y + (transform.FlipY ? region.Height - 1 - dx : dx);
            var syStep = transform.FlipY ? -1 : 1;
            var row0 = unsafe(MemoryMarshal.AsBytes(source.GetRow<TPixel>(sy)));
            var row1 = unsafe(MemoryMarshal.AsBytes(source.GetRow<TPixel>(sy + syStep)));
            var row2 = block == 4 ? unsafe(MemoryMarshal.AsBytes(source.GetRow<TPixel>(sy + (2 * syStep)))) : default;
            var row3 = block == 4 ? unsafe(MemoryMarshal.AsBytes(source.GetRow<TPixel>(sy + (3 * syStep)))) : default;
            for (var j = 0; j < blockRows; j += block)
            {
                // Tile rows j to j + block - 1 read the source columns from firstColumn +- j on: the lowest one is loaded
                // first, and the order of the pixels is reversed for a horizontal mirror
                var column = transform.FlipX ? firstColumn - j - (block - 1) : firstColumn + j;
                var offset = column * Unsafe.SizeOf<TPixel>();
                ref var target = ref unsafe(Unsafe.Add(ref tileStart, (j * tileStride) + (i * Unsafe.SizeOf<TPixel>())));
                if (block == 4)
                {
                    var a = Vector128.Create(row0.Slice(offset, 16)).AsUInt32();
                    var b = Vector128.Create(row1.Slice(offset, 16)).AsUInt32();
                    var c = Vector128.Create(row2.Slice(offset, 16)).AsUInt32();
                    var d = Vector128.Create(row3.Slice(offset, 16)).AsUInt32();
                    if (transform.FlipX)
                    {
                        var reverse = Vector128.Create(3u, 2, 1, 0);
                        a = Vector128.Shuffle(a, reverse);
                        b = Vector128.Shuffle(b, reverse);
                        c = Vector128.Shuffle(c, reverse);
                        d = Vector128.Shuffle(d, reverse);
                    }

                    // a0 b0 a1 b1 | a2 b2 a3 b3 | c0 d0 c1 d1 | c2 d2 c3 d3, then the 64-bit halves
                    var (ab01, ab23) = Interleave32(a, b);
                    var (cd01, cd23) = Interleave32(c, d);
                    var (r0, r1) = Interleave64(ab01.AsUInt64(), cd01.AsUInt64());
                    var (r2, r3) = Interleave64(ab23.AsUInt64(), cd23.AsUInt64());
                    unsafe { r0.AsByte().StoreUnsafe(ref target); }
                    unsafe { r1.AsByte().StoreUnsafe(ref target, (nuint)tileStride); }
                    unsafe { r2.AsByte().StoreUnsafe(ref target, (nuint)(2 * tileStride)); }
                    unsafe { r3.AsByte().StoreUnsafe(ref target, (nuint)(3 * tileStride)); }
                }
                else
                {
                    var a = Vector128.Create(row0.Slice(offset, 16)).AsUInt64();
                    var b = Vector128.Create(row1.Slice(offset, 16)).AsUInt64();
                    var (r0, r1) = transform.FlipX ? Interleave64(Vector128.Shuffle(a, Vector128.Create(1ul, 0)), Vector128.Shuffle(b, Vector128.Create(1ul, 0))) : Interleave64(a, b);
                    unsafe { r0.AsByte().StoreUnsafe(ref target); }
                    unsafe { r1.AsByte().StoreUnsafe(ref target, (nuint)tileStride); }
                }
            }

            // The rows of the tile after the last whole block, pixel by pixel
            for (var k = 0; k < block; k++)
            {
                var row = unsafe(MemoryMarshal.Cast<byte, TPixel>(k switch
                {
                    0 => row0,
                    1 => row1,
                    2 => row2,
                    _ => row3,
                }));
                for (var j = blockRows; j < tileHeight; j++)
                {
                    tile[(j * TileSize) + i + k] = row[firstColumn + (transform.FlipX ? -j : j)];
                }
            }
        }

        return i;
    }

    /// <summary>Interleaves the 32-bit lanes of two vectors: (a0 b0 a1 b1, a2 b2 a3 b3).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<uint> Lower, Vector128<uint> Upper) Interleave32(Vector128<uint> a, Vector128<uint> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return (AdvSimd.Arm64.ZipLow(a, b), AdvSimd.Arm64.ZipHigh(a, b));

        return (Sse2.UnpackLow(a, b), Sse2.UnpackHigh(a, b));
    }

    /// <summary>Interleaves the 64-bit lanes of two vectors: (a0 b0, a1 b1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<ulong> Lower, Vector128<ulong> Upper) Interleave64(Vector128<ulong> a, Vector128<ulong> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return (AdvSimd.Arm64.ZipLow(a, b), AdvSimd.Arm64.ZipHigh(a, b));

        return (Sse2.UnpackLow(a, b), Sse2.UnpackHigh(a, b));
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
