namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the decoded component planes of a JPEG frame into output rows, one band (the rows of an MCU row) at a time:
/// <see cref="JpegRowWriter"/> for 8-bit samples and <see cref="JpegDeepRowWriter"/> for deeper ones.
/// </summary>
internal abstract class JpegBandWriter : IDisposable
{
    private readonly int _height;
    private readonly int _bandRows;

    /// <param name="height">The height of the frame.</param>
    /// <param name="bandRows">The number of rows of an MCU row: <c>8 * Vmax</c>, or <c>Vmax</c> for a lossless frame.</param>
    protected JpegBandWriter(int height, int bandRows)
    {
        _height = height;
        _bandRows = bandRows;
    }

    /// <summary>Gets the number of bands written so far.</summary>
    public int BandsWritten { get; private set; }

    /// <summary>Writes the output rows of the next band (the rows of an MCU row, fewer for the last band).</summary>
    /// <remarks>The planes must hold the band's rows and their context rows (the last row of the previous band and the first row of the next one, when they exist).</remarks>
    public void WriteNextBand(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = BandsWritten * _bandRows;
        WriteRows(first, Math.Min(_height, first + _bandRows));
        BandsWritten++;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes the output rows <paramref name="first"/> (included) to <paramref name="last"/> (excluded).</summary>
    protected abstract void WriteRows(int first, int last);

    /// <summary>Releases the row buffers.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}
