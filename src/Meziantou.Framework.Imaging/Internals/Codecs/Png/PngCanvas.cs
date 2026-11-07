namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A private canvas of wide pixels of the APNG encoder of <see cref="Formats.PngOptimization.MinimumSize"/>: the copy of a
/// displayed frame, or the canvas a frame is drawn on. Its rows are grouped in buffers of about <see cref="BlockBytes"/>
/// bytes (at least one row), rented from the writer scope, so that no buffer holds the whole canvas.
/// </summary>
internal sealed class PngCanvas : PngWideRows, IDisposable
{
    /// <summary>The target size of the buffers of rows.</summary>
    public const int BlockBytes = 1024 * 1024;

    private readonly int _rowLength;
    private readonly int _rowsPerBlock;
    private PooledBuffer?[]? _blocks;

    /// <summary>Creates a transparent black canvas and rents its rows.</summary>
    /// <param name="scope">The writer scope, charged for every buffer.</param>
    /// <param name="wideFormat">The wide format.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds the allocation limit.</exception>
    public PngCanvas(AllocationScope scope, PixelFormat wideFormat, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var rowLength = (long)width * PixelFormats.GetBytesPerPixel(wideFormat);
        if (rowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        _rowLength = (int)rowLength;
        _rowsPerBlock = (int)Math.Clamp(BlockBytes / rowLength, 1, height);
        Width = width;
        Height = height;
        var blockCount = (height + _rowsPerBlock - 1) / _rowsPerBlock;
        _blocks = new PooledBuffer?[blockCount];
        try
        {
            for (var i = 0; i < blockCount; i++)
            {
                var rows = Math.Min(_rowsPerBlock, height - (i * _rowsPerBlock));
                _blocks[i] = scope.Rent(rows * _rowLength, AllocationKind.Temporary);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override int Width { get; }

    public override int Height { get; }

    public override ReadOnlySpan<byte> GetRow(int y) => GetRowSpan(y);

    /// <summary>Gets a writable row.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The row.</returns>
    public Span<byte> GetRowSpan(int y)
    {
        ObjectDisposedException.ThrowIf(_blocks is null, this);
        var block = _blocks[y / _rowsPerBlock]!;
        return block.Span.Slice((y % _rowsPerBlock) * _rowLength, _rowLength);
    }

    /// <summary>Makes a rectangle transparent black (the <c>APNG_DISPOSE_OP_BACKGROUND</c> disposal).</summary>
    /// <param name="region">The rectangle, inside the canvas.</param>
    public void Clear(Rectangle region)
    {
        var bytesPerPixel = _rowLength / Width;
        for (var y = region.Top; y < region.Bottom; y++)
        {
            GetRowSpan(y).Slice(region.X * bytesPerPixel, region.Width * bytesPerPixel).Clear();
        }
    }

    public void Dispose()
    {
        if (_blocks is null)
            return;

        foreach (var block in _blocks)
        {
            block?.Dispose();
        }

        _blocks = null;
    }
}
