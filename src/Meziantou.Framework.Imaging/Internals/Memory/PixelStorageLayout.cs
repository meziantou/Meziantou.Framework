using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The checked geometry of a <see cref="PixelStorage"/>: rows are contiguous, consecutive rows of a slab are
/// <see cref="Stride"/> bytes apart, and the image is split into <see cref="SlabCount"/> row-aligned slabs of
/// <see cref="RowsPerSlab"/> rows (the last slab may hold fewer). The last row of a slab carries no trailing padding.
/// All sizes are 64-bit so that oversized requests are reported instead of overflowing.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PixelStorageLayout
{
    private PixelStorageLayout(int width, int height, int bytesPerPixel, long rowLength, long stride, int rowsPerSlab, int slabCount)
    {
        Width = width;
        Height = height;
        BytesPerPixel = bytesPerPixel;
        RowLength = rowLength;
        Stride = stride;
        RowsPerSlab = rowsPerSlab;
        SlabCount = slabCount;
    }

    public int Width { get; }

    public int Height { get; }

    public int BytesPerPixel { get; }

    /// <summary>Gets the number of visible bytes per row.</summary>
    public long RowLength { get; }

    /// <summary>Gets the distance between the starts of two consecutive rows of the same slab.</summary>
    public long Stride { get; }

    public int RowsPerSlab { get; }

    public int SlabCount { get; }

    /// <summary>Gets the number of rows of the last slab.</summary>
    public int LastSlabRows => Height - ((SlabCount - 1) * RowsPerSlab);

    /// <summary>Gets the length of a full slab (every slab but possibly the last one).</summary>
    public long FullSlabLength => GetSlabLength(RowsPerSlab);

    /// <summary>Gets the length of the last slab.</summary>
    public long LastSlabLength => GetSlabLength(LastSlabRows);

    /// <summary>Gets the length of the largest slab, which must fit in one managed buffer.</summary>
    public long MaxSlabLength => FullSlabLength;

    /// <summary>Computes a layout. Never overflows for <see cref="int"/> dimensions.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the pixel size is not positive.</exception>
    public static PixelStorageLayout Create(int width, int height, int bytesPerPixel, PixelStorageLayoutOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerPixel);
        options ??= PixelStorageLayoutOptions.Default;

        // width * bytesPerPixel < 2^31 * 2^31 cannot overflow; aligning adds less than 4096
        var rowLength = CheckedSizes.GetRowLength(width, bytesPerPixel);
        _ = CheckedSizes.TryAlignUp(rowLength, options.RowAlignment, out var stride);

        // Group as many rows as fit in the target slab ((rows - 1) * stride + rowLength <= target); a slab holds at least one row
        var rowsPerSlab = (int)Math.Clamp(((options.TargetSlabBytes - rowLength) / stride) + 1, 1, height);
        var slabCount = (int)(((long)height + rowsPerSlab - 1) / rowsPerSlab);
        return new PixelStorageLayout(width, height, bytesPerPixel, rowLength, stride, rowsPerSlab, slabCount);
    }

    /// <summary>Computes the bytes charged for all the slabs of this layout when rented from <paramref name="pool"/>.</summary>
    /// <returns><see langword="false"/> if the total overflows <see cref="long"/>.</returns>
    public bool TryGetCapacity(SlabPool pool, out long capacity)
    {
        var full = pool.GetRentCapacity(FullSlabLength);
        var last = pool.GetRentCapacity(LastSlabLength);
        if (!CheckedSizes.TryMultiply(full, SlabCount - 1L, out var fullTotal) || !CheckedSizes.TryAdd(fullTotal, last, out capacity))
        {
            capacity = long.MaxValue;
            return false;
        }

        return true;
    }

    /// <summary>Gets the length of a slab of <paramref name="rows"/> rows.</summary>
    public long GetSlabLength(int rows)
    {
        // (rows - 1) * stride <= TargetSlabBytes when rows > 1, so this cannot overflow
        return ((rows - 1L) * Stride) + RowLength;
    }
}
