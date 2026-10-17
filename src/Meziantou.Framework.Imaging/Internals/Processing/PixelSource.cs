namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixels of a leased frame, read as <see cref="Rgba32"/> rows (<see cref="ImageSimilarity"/>). Neither the lease nor the
/// reader is owned: the caller disposes them.
/// </summary>
internal readonly ref struct PixelSource
{
    private readonly PixelLease _lease;
    private readonly Rgba32RowReader _reader;
    private readonly PixelFormat _format;

    public PixelSource(PixelLease lease, Rgba32RowReader reader, PixelFormat format)
    {
        _lease = lease;
        _reader = reader;
        _format = format;
    }

    public int Width => _lease.Width;

    public int Height => _lease.Height;

    /// <summary>Gets row <paramref name="y"/>, valid until the next call.</summary>
    public ReadOnlySpan<Rgba32> GetRow(int y) => _reader.GetRow(_lease, y);

    /// <summary>Gets a value indicating whether every pixel is fully opaque.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public bool IsOpaque(CancellationToken cancellationToken)
    {
        if (!PixelFormats.HasAlpha(_format))
            return true;

        for (var y = 0; y < Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PixelConverter.IsOpaque(_format, _lease.GetRowBytes(y)))
                return false;
        }

        return true;
    }
}
