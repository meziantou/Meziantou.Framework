namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The working buffers of the inter prediction process, of a size that does not depend on the frame: the two predictions of a
/// block of up to 128x128 samples, the intermediate array of the interpolation filters and the blending mask. Rented by the
/// decoder for the first frame that is predicted from another frame or from itself.
/// </summary>
internal sealed class Av1InterScratch : IDisposable
{
    /// <summary>The number of values of a row of <see cref="GetPrediction"/> and of <see cref="Mask"/>.</summary>
    public const int Stride = Av1Constants.MaxSbSize;

    private const int PredictionLength = Stride * Stride;
    private const int IntermediateLength = Av1InterPredictor.MaxIntermediateHeight * Stride;

    private readonly Av1PooledArray<int> _values;
    private readonly Av1PooledArray<byte> _mask;

    public Av1InterScratch(AllocationScope scope)
    {
        _values = Av1PooledArray<int>.Rent(scope, (2 * PredictionLength) + IntermediateLength, clear: false);
        try
        {
            _mask = Av1PooledArray<byte>.Rent(scope, PredictionLength, clear: false);
        }
        catch
        {
            _values.Dispose();
            throw;
        }
    }

    /// <summary>Gets <c>preds[ refList ]</c>: <see cref="Stride"/> values per row.</summary>
    public Span<int> GetPrediction(int refList) => _values.Span.Slice(refList * PredictionLength, PredictionLength);

    /// <summary>Gets the intermediate array of the block inter prediction process.</summary>
    public Span<int> Intermediate => _values.Span.Slice(2 * PredictionLength, IntermediateLength);

    /// <summary>Gets <c>Mask</c>: <see cref="Stride"/> weights per row.</summary>
    public Span<byte> Mask => _mask.Span;

    public void Dispose()
    {
        _values.Dispose();
        _mask.Dispose();
    }
}
