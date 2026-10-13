namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The working buffers of the decoding of a transform block, of a size that does not depend on the picture: its transform
/// coefficient levels, its residual samples, and the residual samples of the luma block that the cross-component
/// prediction reads. Rented once by the decoder and reused by every slice segment.
/// </summary>
internal sealed class HevcSliceScratch : IDisposable
{
    /// <summary>The number of samples of the greatest prediction block: 64x64.</summary>
    public const int PredictionLength = 64 * 64;

    /// <summary>The number of reference samples of the greatest prediction block: the block and the 7 rows and columns that its interpolation reads.</summary>
    public const int WindowLength = 71 * 71;

    private const int BlockLength = 32 * 32;

    private readonly Av1PooledArray<int> _values;
    private readonly AllocationScope _scope;
    private Av1PooledArray<short>? _prediction;
    private Av1PooledArray<ushort>? _window;

    public HevcSliceScratch(AllocationScope scope)
    {
        _scope = scope;
        _values = Av1PooledArray<int>.Rent(scope, 4 * BlockLength);
    }

    /// <summary>Gets <c>TransCoeffLevel</c> of the transform block being read: the rows of the block, <c>nTbS</c> values each.</summary>
    public Span<int> Coefficients => _values.Span[..BlockLength];

    /// <summary>Gets the scratch of <see cref="HevcInverseTransform.Transform"/>.</summary>
    public Span<int> Transform => _values.Span.Slice(BlockLength, BlockLength);

    /// <summary>Gets the residual samples of the luma transform block of the transform unit being decoded.</summary>
    public Span<int> LumaResidual => _values.Span.Slice(2 * BlockLength, BlockLength);

    /// <summary>Gets the residual samples of the chroma transform block being decoded with cross-component prediction.</summary>
    public Span<int> ChromaResidual => _values.Span.Slice(3 * BlockLength, BlockLength);

    /// <summary>Gets the prediction samples of a list (<c>predSamplesL0</c>, <c>predSamplesL1</c>) of the prediction block being decoded.</summary>
    public Span<short> GetPrediction(int list) => RentPrediction().Span.Slice(list * PredictionLength, PredictionLength);

    /// <summary>Gets the rows that the vertical interpolation filter reads: 71 rows of 64 values.</summary>
    public Span<short> Intermediate => RentPrediction().Span.Slice(2 * PredictionLength, 71 * 64);

    /// <summary>Gets the copy of the reference samples of a prediction block that reaches beyond the reference picture.</summary>
    public Span<ushort> Window => (_window ??= Av1PooledArray<ushort>.Rent(_scope, WindowLength, clear: false)).Span;

    private Av1PooledArray<short> RentPrediction() => _prediction ??= Av1PooledArray<short>.Rent(_scope, (2 * PredictionLength) + (71 * 64), clear: false);

    public void Dispose()
    {
        _values.Dispose();
        _prediction?.Dispose();
        _window?.Dispose();
    }
}
