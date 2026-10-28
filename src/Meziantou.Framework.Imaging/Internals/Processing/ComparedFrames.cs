namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The two leased frames of a comparison read as <see cref="Rgba32"/> rows by several workers (<see cref="PsnrCalculator"/>,
/// <see cref="SsimAccumulator"/>): each worker has a pair of row readers of its own, rented on the calling thread. The leases
/// are not owned: the caller keeps them while the rows are read, and disposes them.
/// </summary>
internal sealed class ComparedFrames : IDisposable
{
    private readonly PixelStorage _expected;
    private readonly PixelStorage _actual;
    private readonly Rgba32RowReader?[] _expectedReaders;
    private readonly Rgba32RowReader?[] _actualReaders;

    /// <summary>Rents the row readers of <paramref name="workers"/> workers.</summary>
    /// <exception cref="ImageResourceLimitException">The row buffers exceed the allocation limit.</exception>
    public ComparedFrames(AllocationScope scope, PixelStorage expected, PixelFormat expectedFormat, PixelStorage actual, PixelFormat actualFormat, int workers)
    {
        _expected = expected;
        _actual = actual;
        _expectedReaders = new Rgba32RowReader?[workers];
        _actualReaders = new Rgba32RowReader?[workers];
        try
        {
            for (var worker = 0; worker < workers; worker++)
            {
                _expectedReaders[worker] = Rgba32RowReader.Create(scope, expectedFormat, expected.Width);
                _actualReaders[worker] = Rgba32RowReader.Create(scope, actualFormat, actual.Width);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width => _expected.Width;

    public int Height => _expected.Height;

    public Size Size => new(Width, Height);

    /// <summary>Gets row <paramref name="y"/> of the expected frame, valid until the next call of the same worker.</summary>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample of the row is not a number in [0, 1].</exception>
    public ReadOnlySpan<Rgba32> GetExpectedRow(int worker, int y) => GetReader(_expectedReaders, worker).GetRow(_expected, y);

    /// <summary>Gets row <paramref name="y"/> of the actual frame, valid until the next call of the same worker.</summary>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample of the row is not a number in [0, 1].</exception>
    public ReadOnlySpan<Rgba32> GetActualRow(int worker, int y) => GetReader(_actualReaders, worker).GetRow(_actual, y);

    public void Dispose()
    {
        for (var worker = 0; worker < _expectedReaders.Length; worker++)
        {
            _expectedReaders[worker]?.Dispose();
            _expectedReaders[worker] = null;
            _actualReaders[worker]?.Dispose();
            _actualReaders[worker] = null;
        }
    }

    private static Rgba32RowReader GetReader(Rgba32RowReader?[] readers, int worker) => readers[worker] ?? throw new ObjectDisposedException(nameof(ComparedFrames));
}
