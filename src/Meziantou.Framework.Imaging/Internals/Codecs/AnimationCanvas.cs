namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A private canvas of an animation encoder (<see cref="AnimationFrameDelta"/>): rows of straight-alpha RGBA pixels of 4 or
/// 8 bytes, grouped in buffers of about <see cref="BlockBytes"/> bytes (at least one row) rented from the writer scope, so
/// that no buffer holds the whole canvas. It starts transparent black.
/// </summary>
internal sealed class AnimationCanvas : IDisposable
{
    /// <summary>The target size of the buffers of rows.</summary>
    public const int BlockBytes = 1024 * 1024;

    private readonly int _rowLength;
    private readonly int _rowsPerBlock;
    private PooledBuffer?[]? _blocks;

    /// <summary>Creates a transparent black canvas and rents its rows.</summary>
    /// <param name="scope">The writer scope, charged for every buffer.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="bytesPerPixel">4 or 8.</param>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds the allocation limit.</exception>
    public AnimationCanvas(AllocationScope scope, int width, int height, int bytesPerPixel)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var rowLength = (long)width * bytesPerPixel;
        if (rowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        _rowLength = (int)rowLength;
        _rowsPerBlock = (int)Math.Clamp(BlockBytes / rowLength, 1, height);
        Width = width;
        Height = height;
        BytesPerPixel = bytesPerPixel;
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

    public int Width { get; }

    public int Height { get; }

    public int BytesPerPixel { get; }

    /// <summary>Gets a row.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The row, <see cref="Width"/> pixels.</returns>
    public Span<byte> GetRow(int y)
    {
        ObjectDisposedException.ThrowIf(_blocks is null, this);
        var block = _blocks[y / _rowsPerBlock]!;
        return block.Span.Slice((y % _rowsPerBlock) * _rowLength, _rowLength);
    }

    /// <summary>Makes a rectangle transparent black.</summary>
    /// <param name="region">The rectangle, inside the canvas.</param>
    public void Clear(Rectangle region)
    {
        for (var y = region.Top; y < region.Bottom; y++)
        {
            GetRow(y).Slice(region.X * BytesPerPixel, region.Width * BytesPerPixel).Clear();
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
