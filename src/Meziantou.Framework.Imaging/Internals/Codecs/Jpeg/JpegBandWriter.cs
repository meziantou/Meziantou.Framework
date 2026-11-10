namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the decoded component planes of a JPEG frame into output rows, one band (the rows of an MCU row) at a time:
/// <see cref="JpegRowWriter"/> for 8-bit samples and <see cref="JpegDeepRowWriter"/> for deeper ones.
/// </summary>
/// <remarks>
/// <para>
/// Several writers of the same planes write the bands of a frame at the same time (<see cref="WriteBands"/>): each one has
/// its own rows of upsampled and converted samples, and they only read the planes.
/// </para>
/// <para>
/// A writer writes the rows and the columns of its window only (<see cref="ImageDecodeRequest.Region"/>): row <c>y</c> of
/// the frame is row <c>y - Top</c> of the image, which starts at column <see cref="Left"/> of the frame. The rows of a band
/// outside the window are not written, and their planes are not read.
/// </para>
/// </remarks>
internal abstract class JpegBandWriter : IDisposable
{
    private readonly int _height;
    private readonly int _bandRows;
    private readonly Rectangle _window;

    /// <param name="height">The height of the frame.</param>
    /// <param name="bandRows">The number of rows of an MCU row: <c>8 * Vmax</c>, or <c>Vmax</c> for a lossless frame.</param>
    /// <param name="window">The area of the frame written to the image, inside the frame.</param>
    protected JpegBandWriter(int height, int bandRows, Rectangle window)
    {
        _height = height;
        _bandRows = bandRows;
        _window = window;
    }

    /// <summary>Gets the first column of the frame written to the image.</summary>
    protected int Left => _window.X;

    /// <summary>Gets the number of columns written to the image.</summary>
    protected int Columns => _window.Width;

    /// <summary>Gets the row of the frame written to the first row of the image.</summary>
    protected int Top => _window.Y;

    /// <summary>Gets the number of bands written so far.</summary>
    public int BandsWritten { get; private set; }

    /// <summary>Writes the output rows of the next band (the rows of an MCU row, fewer for the last band).</summary>
    /// <remarks>The planes must hold the band's rows and their context rows (the last row of the previous band and the first row of the next one, when they exist).</remarks>
    public void WriteNextBand(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (first, last) = GetWindowRows(BandsWritten);
        if (first < last)
        {
            WriteRows(first, last);
        }

        BandsWritten++;
    }

    /// <summary>
    /// Writes the bands from <see cref="BandsWritten"/> of the first writer to <paramref name="endBand"/> (excluded) with
    /// several writers of the same planes, one per worker, each one a contiguous range of the bands: the rows are those of
    /// one writer, and the error is the one of the first band that fails (a cancellation first). The image is leased on the
    /// calling thread.
    /// </summary>
    /// <param name="writers">The writers, the first one counting the bands written; they write into an image.</param>
    /// <param name="endBand">The band after the last one to write.</param>
    /// <param name="cancellationToken">Checked before each band.</param>
    public static void WriteBands(JpegBandWriter[] writers, int endBand, CancellationToken cancellationToken)
    {
        var main = writers[0];
        var first = main.BandsWritten;
        var count = endBand - first;
        if (count <= 0)
            return;

        var workers = Math.Min(writers.Length, count);
        using (var lease = main.LeaseImage())
        {
            var storage = lease.Storage;
            RowBands.RunParallelInBandOrder(workers, worker =>
            {
                var writer = writers[worker];
                var end = first + RowBands.GetBandStart(count, worker + 1, workers);
                for (var band = first + RowBands.GetBandStart(count, worker, workers); band < end; band++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (firstRow, lastRow) = main.GetWindowRows(band);
                    if (firstRow < lastRow)
                    {
                        writer.WriteRows(storage, firstRow, lastRow);
                    }
                }
            });
        }

        main.BandsWritten = endBand;
    }

    /// <summary>Gets the rows of a band that are in the window: none when the first one is not before the last one.</summary>
    private (int First, int Last) GetWindowRows(int band)
    {
        var first = band * _bandRows;
        return (Math.Max(first, _window.Top), Math.Min(Math.Min(_height, first + _bandRows), _window.Bottom));
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes the output rows <paramref name="first"/> (included) to <paramref name="last"/> (excluded).</summary>
    protected abstract void WriteRows(int first, int last);

    /// <summary>Writes the output rows <paramref name="first"/> (included) to <paramref name="last"/> (excluded) of an image leased by the caller.</summary>
    /// <param name="storage">The pixels of the image.</param>
    /// <param name="first">The first row.</param>
    /// <param name="last">The row after the last one.</param>
    protected abstract void WriteRows(PixelStorage storage, int first, int last);

    /// <summary>Leases the image the rows are written to.</summary>
    /// <exception cref="InvalidOperationException">The rows are handed to the container of an embedded datastream.</exception>
    protected abstract PixelLease LeaseImage();

    /// <summary>Releases the row buffers.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}
