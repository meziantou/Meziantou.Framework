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
/// vectors, quarter turns of 1-, 2-, 3-, 4- and 8-byte pixels transpose square blocks in 128-bit registers and grayscale
/// computes 4 lumas at a time; both give the results of the scalar loops (<c>VectorizedKernelTests</c>).
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
                    i = Unsafe.SizeOf<TPixel>() switch
                    {
                        1 => FillTileBlocks1(source, tile, region, transform, tileX, tileWidth, tileHeight, firstColumn),
                        2 => FillTileBlocks2(source, tile, region, transform, tileX, tileWidth, tileHeight, firstColumn),
                        3 => FillTileBlocks3(source, tile, region, transform, tileX, tileWidth, tileHeight, firstColumn),
                        _ => FillTileBlocks(source, tile, region, transform, tileX, tileWidth, tileHeight, firstColumn),
                    };
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
        => BitConverter.IsLittleEndian && Unsafe.SizeOf<TPixel>() switch
        {
            1 or 2 or 4 or 8 => AdvSimd.Arm64.IsSupported || Sse2.IsSupported,
            3 => AdvSimd.Arm64.IsSupported || Ssse3.IsSupported,
            _ => false,
        };

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

    /// <summary>
    /// Fills the leading columns of a tile of 1-byte pixels by groups of 16 source rows: 16x16 blocks of pixels are loaded
    /// from the rows, transposed in 128-bit registers (interleaving 8-, 16-, 32- and 64-bit lanes) and stored to
    /// consecutive tile rows, from the last one up for a horizontal mirror. Every tile pixel is the copy the scalar loop
    /// makes.
    /// </summary>
    /// <returns>The number of tile columns filled; the rows of the tile beyond the last whole block are filled too.</returns>
    private static int FillTileBlocks1<TPixel>(scoped in PixelLease source, Span<TPixel> tile, Rectangle region, OrientationTransform transform, int tileX, int tileWidth, int tileHeight, int firstColumn)
        where TPixel : unmanaged
    {
        Debug.Assert(Unsafe.SizeOf<TPixel>() == 1);
        const int Block = 16;
        var blockRows = tileHeight / Block * Block;
        ref var tileStart = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(tile)));
        var (firstRow, rowStep) = transform.FlipX ? ((Block - 1) * TileSize, -TileSize) : (0, TileSize);
        var syStep = transform.FlipY ? -1 : 1;
        var i = 0;
        for (; i + Block <= tileWidth; i += Block)
        {
            // The source rows of tile columns i to i + 15
            var dx = tileX + i;
            var sy = region.Y + (transform.FlipY ? region.Height - 1 - dx : dx);
            var row0 = source.GetRowBytes(sy);
            var row1 = source.GetRowBytes(sy + syStep);
            var row2 = source.GetRowBytes(sy + (2 * syStep));
            var row3 = source.GetRowBytes(sy + (3 * syStep));
            var row4 = source.GetRowBytes(sy + (4 * syStep));
            var row5 = source.GetRowBytes(sy + (5 * syStep));
            var row6 = source.GetRowBytes(sy + (6 * syStep));
            var row7 = source.GetRowBytes(sy + (7 * syStep));
            var row8 = source.GetRowBytes(sy + (8 * syStep));
            var row9 = source.GetRowBytes(sy + (9 * syStep));
            var row10 = source.GetRowBytes(sy + (10 * syStep));
            var row11 = source.GetRowBytes(sy + (11 * syStep));
            var row12 = source.GetRowBytes(sy + (12 * syStep));
            var row13 = source.GetRowBytes(sy + (13 * syStep));
            var row14 = source.GetRowBytes(sy + (14 * syStep));
            var row15 = source.GetRowBytes(sy + (15 * syStep));
            for (var j = 0; j < blockRows; j += Block)
            {
                // Tile rows j to j + 15 read the source columns from firstColumn +- j on: the lowest one is loaded first
                var column = transform.FlipX ? firstColumn - j - (Block - 1) : firstColumn + j;

                // Columns 0-7 and 8-15 of rows (0, 1), (2, 3)... as 16-bit lanes
                var (a0, a1) = Interleave8(Vector128.Create(row0.Slice(column, 16)), Vector128.Create(row1.Slice(column, 16)));
                var (a2, a3) = Interleave8(Vector128.Create(row2.Slice(column, 16)), Vector128.Create(row3.Slice(column, 16)));
                var (a4, a5) = Interleave8(Vector128.Create(row4.Slice(column, 16)), Vector128.Create(row5.Slice(column, 16)));
                var (a6, a7) = Interleave8(Vector128.Create(row6.Slice(column, 16)), Vector128.Create(row7.Slice(column, 16)));
                var (a8, a9) = Interleave8(Vector128.Create(row8.Slice(column, 16)), Vector128.Create(row9.Slice(column, 16)));
                var (a10, a11) = Interleave8(Vector128.Create(row10.Slice(column, 16)), Vector128.Create(row11.Slice(column, 16)));
                var (a12, a13) = Interleave8(Vector128.Create(row12.Slice(column, 16)), Vector128.Create(row13.Slice(column, 16)));
                var (a14, a15) = Interleave8(Vector128.Create(row14.Slice(column, 16)), Vector128.Create(row15.Slice(column, 16)));

                // Columns 0-3, 4-7, 8-11 and 12-15 of rows 0 to 3, 4 to 7, 8 to 11 and 12 to 15 as 32-bit lanes
                var (b0, b1) = Interleave16(a0.AsUInt16(), a2.AsUInt16());
                var (b2, b3) = Interleave16(a1.AsUInt16(), a3.AsUInt16());
                var (b4, b5) = Interleave16(a4.AsUInt16(), a6.AsUInt16());
                var (b6, b7) = Interleave16(a5.AsUInt16(), a7.AsUInt16());
                var (b8, b9) = Interleave16(a8.AsUInt16(), a10.AsUInt16());
                var (b10, b11) = Interleave16(a9.AsUInt16(), a11.AsUInt16());
                var (b12, b13) = Interleave16(a12.AsUInt16(), a14.AsUInt16());
                var (b14, b15) = Interleave16(a13.AsUInt16(), a15.AsUInt16());

                // Columns 0-1, 2-3... 14-15 of rows 0 to 7 and 8 to 15 as 64-bit lanes
                var (c0, c1) = Interleave32(b0.AsUInt32(), b4.AsUInt32());
                var (c2, c3) = Interleave32(b1.AsUInt32(), b5.AsUInt32());
                var (c4, c5) = Interleave32(b2.AsUInt32(), b6.AsUInt32());
                var (c6, c7) = Interleave32(b3.AsUInt32(), b7.AsUInt32());
                var (c8, c9) = Interleave32(b8.AsUInt32(), b12.AsUInt32());
                var (c10, c11) = Interleave32(b9.AsUInt32(), b13.AsUInt32());
                var (c12, c13) = Interleave32(b10.AsUInt32(), b14.AsUInt32());
                var (c14, c15) = Interleave32(b11.AsUInt32(), b15.AsUInt32());

                // Column t of the 16 rows
                var (r0, r1) = Interleave64(c0.AsUInt64(), c8.AsUInt64());
                var (r2, r3) = Interleave64(c1.AsUInt64(), c9.AsUInt64());
                var (r4, r5) = Interleave64(c2.AsUInt64(), c10.AsUInt64());
                var (r6, r7) = Interleave64(c3.AsUInt64(), c11.AsUInt64());
                var (r8, r9) = Interleave64(c4.AsUInt64(), c12.AsUInt64());
                var (r10, r11) = Interleave64(c5.AsUInt64(), c13.AsUInt64());
                var (r12, r13) = Interleave64(c6.AsUInt64(), c14.AsUInt64());
                var (r14, r15) = Interleave64(c7.AsUInt64(), c15.AsUInt64());

                ref var target = ref unsafe(Unsafe.Add(ref tileStart, (j * TileSize) + i + firstRow));
                unsafe
                {
                    r0.AsByte().StoreUnsafe(ref target);
                    r1.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, rowStep));
                    r2.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 2 * rowStep));
                    r3.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 3 * rowStep));
                    r4.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 4 * rowStep));
                    r5.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 5 * rowStep));
                    r6.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 6 * rowStep));
                    r7.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 7 * rowStep));
                    r8.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 8 * rowStep));
                    r9.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 9 * rowStep));
                    r10.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 10 * rowStep));
                    r11.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 11 * rowStep));
                    r12.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 12 * rowStep));
                    r13.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 13 * rowStep));
                    r14.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 14 * rowStep));
                    r15.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 15 * rowStep));
                }
            }

            FillBlockRemainder(source, tile, transform, i, Block, sy, blockRows, tileHeight, firstColumn);
        }

        return i;
    }

    /// <summary>
    /// Fills the leading columns of a tile of 2-byte pixels by groups of 8 source rows: 8x8 blocks of pixels are loaded
    /// from the rows, transposed in 128-bit registers (interleaving 16-, 32- and 64-bit lanes) and stored to consecutive
    /// tile rows, from the last one up for a horizontal mirror. Every tile pixel is the copy the scalar loop makes.
    /// </summary>
    /// <returns>The number of tile columns filled; the rows of the tile beyond the last whole block are filled too.</returns>
    private static int FillTileBlocks2<TPixel>(scoped in PixelLease source, Span<TPixel> tile, Rectangle region, OrientationTransform transform, int tileX, int tileWidth, int tileHeight, int firstColumn)
        where TPixel : unmanaged
    {
        Debug.Assert(Unsafe.SizeOf<TPixel>() == 2);
        const int Block = 8;
        var blockRows = tileHeight / Block * Block;
        ref var tileStart = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(tile)));
        var (firstRow, rowStep) = transform.FlipX ? ((Block - 1) * TileSize * 2, -TileSize * 2) : (0, TileSize * 2);
        var syStep = transform.FlipY ? -1 : 1;
        var i = 0;
        for (; i + Block <= tileWidth; i += Block)
        {
            // The source rows of tile columns i to i + 7
            var dx = tileX + i;
            var sy = region.Y + (transform.FlipY ? region.Height - 1 - dx : dx);
            var row0 = source.GetRowBytes(sy);
            var row1 = source.GetRowBytes(sy + syStep);
            var row2 = source.GetRowBytes(sy + (2 * syStep));
            var row3 = source.GetRowBytes(sy + (3 * syStep));
            var row4 = source.GetRowBytes(sy + (4 * syStep));
            var row5 = source.GetRowBytes(sy + (5 * syStep));
            var row6 = source.GetRowBytes(sy + (6 * syStep));
            var row7 = source.GetRowBytes(sy + (7 * syStep));
            for (var j = 0; j < blockRows; j += Block)
            {
                // Tile rows j to j + 7 read the source columns from firstColumn +- j on: the lowest one is loaded first
                var offset = 2 * (transform.FlipX ? firstColumn - j - (Block - 1) : firstColumn + j);

                // Columns 0-3 and 4-7 of rows (0, 1), (2, 3)... as 32-bit lanes
                var (a0, a1) = Interleave16(Vector128.Create(row0.Slice(offset, 16)).AsUInt16(), Vector128.Create(row1.Slice(offset, 16)).AsUInt16());
                var (a2, a3) = Interleave16(Vector128.Create(row2.Slice(offset, 16)).AsUInt16(), Vector128.Create(row3.Slice(offset, 16)).AsUInt16());
                var (a4, a5) = Interleave16(Vector128.Create(row4.Slice(offset, 16)).AsUInt16(), Vector128.Create(row5.Slice(offset, 16)).AsUInt16());
                var (a6, a7) = Interleave16(Vector128.Create(row6.Slice(offset, 16)).AsUInt16(), Vector128.Create(row7.Slice(offset, 16)).AsUInt16());

                // Columns 0-1, 2-3, 4-5 and 6-7 of rows 0 to 3 and 4 to 7 as 64-bit lanes
                var (b0, b1) = Interleave32(a0.AsUInt32(), a2.AsUInt32());
                var (b2, b3) = Interleave32(a1.AsUInt32(), a3.AsUInt32());
                var (b4, b5) = Interleave32(a4.AsUInt32(), a6.AsUInt32());
                var (b6, b7) = Interleave32(a5.AsUInt32(), a7.AsUInt32());

                // Column t of the 8 rows
                var (r0, r1) = Interleave64(b0.AsUInt64(), b4.AsUInt64());
                var (r2, r3) = Interleave64(b1.AsUInt64(), b5.AsUInt64());
                var (r4, r5) = Interleave64(b2.AsUInt64(), b6.AsUInt64());
                var (r6, r7) = Interleave64(b3.AsUInt64(), b7.AsUInt64());

                ref var target = ref unsafe(Unsafe.Add(ref tileStart, (j * TileSize * 2) + (i * 2) + firstRow));
                unsafe
                {
                    r0.AsByte().StoreUnsafe(ref target);
                    r1.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, rowStep));
                    r2.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 2 * rowStep));
                    r3.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 3 * rowStep));
                    r4.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 4 * rowStep));
                    r5.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 5 * rowStep));
                    r6.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 6 * rowStep));
                    r7.AsByte().StoreUnsafe(ref Unsafe.Add(ref target, 7 * rowStep));
                }
            }

            FillBlockRemainder(source, tile, transform, i, Block, sy, blockRows, tileHeight, firstColumn);
        }

        return i;
    }

    /// <summary>
    /// Fills the leading columns of a tile of 3-byte pixels by groups of 4 source rows: 4x4 blocks of pixels are loaded
    /// from the rows (12 bytes each), widened to 32-bit lanes, transposed in 128-bit registers, packed back and stored to
    /// consecutive tile rows (12 bytes each), from the last one up for a horizontal mirror. Every tile pixel is the copy the
    /// scalar loop makes.
    /// </summary>
    /// <returns>The number of tile columns filled; the rows of the tile beyond the last whole block are filled too.</returns>
    private static int FillTileBlocks3<TPixel>(scoped in PixelLease source, Span<TPixel> tile, Rectangle region, OrientationTransform transform, int tileX, int tileWidth, int tileHeight, int firstColumn)
        where TPixel : unmanaged
    {
        Debug.Assert(Unsafe.SizeOf<TPixel>() == 3);
        const int Block = 4;
        var expand = Vector128.Create((byte)0, 1, 2, 0xFF, 3, 4, 5, 0xFF, 6, 7, 8, 0xFF, 9, 10, 11, 0xFF);
        var pack = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);
        var blockRows = tileHeight / Block * Block;
        ref var tileStart = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(tile)));
        var (firstRow, rowStep) = transform.FlipX ? ((Block - 1) * TileSize * 3, -TileSize * 3) : (0, TileSize * 3);
        var syStep = transform.FlipY ? -1 : 1;
        var i = 0;
        for (; i + Block <= tileWidth; i += Block)
        {
            // The source rows of tile columns i to i + 3
            var dx = tileX + i;
            var sy = region.Y + (transform.FlipY ? region.Height - 1 - dx : dx);
            var row0 = source.GetRowBytes(sy);
            var row1 = source.GetRowBytes(sy + syStep);
            var row2 = source.GetRowBytes(sy + (2 * syStep));
            var row3 = source.GetRowBytes(sy + (3 * syStep));
            for (var j = 0; j < blockRows; j += Block)
            {
                // Tile rows j to j + 3 read the source columns from firstColumn +- j on: the lowest one is loaded first
                var offset = 3 * (transform.FlipX ? firstColumn - j - (Block - 1) : firstColumn + j);
                var a = Vector128.Shuffle(Load12(row0, offset), expand).AsUInt32();
                var b = Vector128.Shuffle(Load12(row1, offset), expand).AsUInt32();
                var c = Vector128.Shuffle(Load12(row2, offset), expand).AsUInt32();
                var d = Vector128.Shuffle(Load12(row3, offset), expand).AsUInt32();
                var (ab01, ab23) = Interleave32(a, b);
                var (cd01, cd23) = Interleave32(c, d);
                var (r0, r1) = Interleave64(ab01.AsUInt64(), cd01.AsUInt64());
                var (r2, r3) = Interleave64(ab23.AsUInt64(), cd23.AsUInt64());

                ref var target = ref unsafe(Unsafe.Add(ref tileStart, (j * TileSize * 3) + (i * 3) + firstRow));
                Store12(Vector128.Shuffle(r0.AsByte(), pack), ref target);
                Store12(Vector128.Shuffle(r1.AsByte(), pack), ref unsafe(Unsafe.Add(ref target, rowStep)));
                Store12(Vector128.Shuffle(r2.AsByte(), pack), ref unsafe(Unsafe.Add(ref target, 2 * rowStep)));
                Store12(Vector128.Shuffle(r3.AsByte(), pack), ref unsafe(Unsafe.Add(ref target, 3 * rowStep)));
            }

            FillBlockRemainder(source, tile, transform, i, Block, sy, blockRows, tileHeight, firstColumn);
        }

        return i;
    }

    /// <summary>Loads 12 bytes of a row to the low lanes of a vector (the last 4 bytes are zero).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Load12(Span<byte> row, int offset)
    {
        ref var start = ref unsafe(MemoryMarshal.GetReference(row.Slice(offset, 12)));
        return Vector128.Create(unsafe(Unsafe.ReadUnaligned<ulong>(ref start)), unsafe(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref start, 8)))).AsByte();
    }

    /// <summary>Stores the 12 low bytes of a vector.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store12(Vector128<byte> value, ref byte target)
    {
        unsafe { Unsafe.WriteUnaligned(ref target, value.AsUInt64().ToScalar()); }
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, 8), value.AsUInt32().GetElement(2)); }
    }

    /// <summary>
    /// Fills the tile rows after the last whole block of tile columns <paramref name="i"/> to <paramref name="i"/> +
    /// <paramref name="block"/> - 1, pixel by pixel.
    /// </summary>
    private static void FillBlockRemainder<TPixel>(scoped in PixelLease source, Span<TPixel> tile, OrientationTransform transform, int i, int block, int sy, int blockRows, int tileHeight, int firstColumn)
        where TPixel : unmanaged
    {
        if (blockRows == tileHeight)
            return;

        var syStep = transform.FlipY ? -1 : 1;
        for (var k = 0; k < block; k++)
        {
            var row = source.GetRow<TPixel>(sy + (k * syStep));
            for (var j = blockRows; j < tileHeight; j++)
            {
                tile[(j * TileSize) + i + k] = row[firstColumn + (transform.FlipX ? -j : j)];
            }
        }
    }

    /// <summary>Interleaves the 8-bit lanes of two vectors: (a0 b0 ... a7 b7, a8 b8 ... a15 b15).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<byte> Lower, Vector128<byte> Upper) Interleave8(Vector128<byte> a, Vector128<byte> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return (AdvSimd.Arm64.ZipLow(a, b), AdvSimd.Arm64.ZipHigh(a, b));

        return (Sse2.UnpackLow(a, b), Sse2.UnpackHigh(a, b));
    }

    /// <summary>Interleaves the 16-bit lanes of two vectors: (a0 b0 a1 b1 a2 b2 a3 b3, a4 b4 a5 b5 a6 b6 a7 b7).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<ushort> Lower, Vector128<ushort> Upper) Interleave16(Vector128<ushort> a, Vector128<ushort> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return (AdvSimd.Arm64.ZipLow(a, b), AdvSimd.Arm64.ZipHigh(a, b));

        return (Sse2.UnpackLow(a, b), Sse2.UnpackHigh(a, b));
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
    /// (<see cref="PixelConverter.Luma8Vector(Vector128{byte}, bool)"/>, <see cref="PixelConverter.Luma16Vector"/>: the results of
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
            // x64: the same operations on every lane of 256-bit and 512-bit vectors (8 and 16 pixels)
            if (Vector512.IsHardwareAccelerated)
            {
                for (; i + 16 <= length; i += 16)
                {
                    var pixels = unsafe(Vector512.LoadUnsafe(ref start, (nuint)(4 * i)));
                    var luma = PixelConverter.Luma8Vector(pixels, bgr: typeof(TPixel) == typeof(Bgra32)).AsUInt32();
                    var result = (luma * 0x010101u) | (pixels.AsUInt32() & Vector512.Create(0xFF000000u));
                    unsafe { result.AsByte().StoreUnsafe(ref start, (nuint)(4 * i)); }
                }
            }

            if (Vector256.IsHardwareAccelerated)
            {
                for (; i + 8 <= length; i += 8)
                {
                    var pixels = unsafe(Vector256.LoadUnsafe(ref start, (nuint)(4 * i)));
                    var luma = PixelConverter.Luma8Vector(pixels, bgr: typeof(TPixel) == typeof(Bgra32)).AsUInt32();
                    var result = (luma * 0x010101u) | (pixels.AsUInt32() & Vector256.Create(0xFF000000u));
                    unsafe { result.AsByte().StoreUnsafe(ref start, (nuint)(4 * i)); }
                }
            }

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
            if (Avx2.IsSupported)
            {
                // x64: 8 pixels, 4 per 128-bit lane (each lane read from the 12 bytes of its pixels, 16 bytes at both
                // positions); the 24 bytes of the result are moved together and written as 16 + 8 bytes
                var expand256 = Vector256.Create(expand, expand);
                var pack256 = Vector256.Create(pack, pack);
                var groups = Vector256.Create(0, 1, 2, 4, 5, 6, 7, 7);
                for (; i + 10 <= length; i += 8)
                {
                    var loaded = Vector256.Create(unsafe(Vector128.LoadUnsafe(ref start, (nuint)(3 * i))), unsafe(Vector128.LoadUnsafe(ref start, (nuint)((3 * i) + 12))));
                    var pixels = Avx2.Shuffle(loaded, expand256);
                    var luma = PixelConverter.Luma8Vector(pixels, bgr: false).AsUInt32();
                    var result = Avx2.PermuteVar8x32(Avx2.Shuffle((luma * 0x010101u).AsByte(), pack256).AsInt32(), groups).AsByte();
                    ref var output = ref unsafe(Unsafe.Add(ref start, 3 * i));
                    unsafe { result.GetLower().StoreUnsafe(ref output); }
                    unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, 16), result.AsUInt64().GetElement(2)); }
                }
            }

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
