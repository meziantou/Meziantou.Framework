using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The reconstruction of a JPEG frame by several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>), once the
/// quantized coefficients of its blocks are known: the coefficients of a progressive frame at its end, or a ring of the MCU
/// rows of a sequential frame that the scan decoder fills (<see cref="JpegSequentialScanDecoder"/>). The entropy-coded data
/// is decoded by the calling thread, then each batch of MCU rows is transformed and written in two steps:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// The inverse DCT of the blocks of the rows of the batch, a contiguous range of rows per worker: a block depends only on its
/// own coefficients, and the rows write different bands of the planes. The planes are rings of
/// <see cref="GetPlaneBands"/> bands: the rows of the batch, and the two rows before them, which the first band written
/// next still reads.
/// </description></item>
/// <item><description>
/// The output rows of the bands that have their lower context row (<see cref="JpegBandWriter.WriteBands"/>): a band reads
/// the planes from the last sample row of the band above to the first sample row of the band below, and writes its own
/// rows of the image; each worker has a writer of its own (rows of upsampled and converted samples).
/// </description></item>
/// </list>
/// <para>
/// The samples are those of the transform of each block as it is decoded (<see cref="JpegScanDecoder"/>): the same
/// dequantization, the same DC-only and sparse shortcuts, the same arithmetic; the rows written after each MCU row are
/// those of one worker, so a rendering of a frame that ends early shows the same rows. Nothing here can fail but a
/// cancellation.
/// </para>
/// </remarks>
internal sealed class JpegReconstruction
{
    /// <summary>The number of MCU rows that each worker transforms in a batch of a sequential frame.</summary>
    internal const int RowsPerWorker = 4;

    private readonly JpegFrameComponent[] _components;
    private readonly JpegBandWriter[] _writers;
    private readonly int _mcuRows;
    private readonly ushort[][] _quantization;
    private readonly double[][] _blocks;
    private readonly double[][] _scales;
    private readonly CancellationToken _cancellationToken;

    /// <param name="components">The components, whose planes hold <see cref="GetPlaneBands"/> bands.</param>
    /// <param name="writers">The writers of the bands, one per worker.</param>
    /// <param name="quantization">The quantization table of each component, in natural order.</param>
    /// <param name="mcuRows">The number of MCU rows of the frame.</param>
    /// <param name="batchRows">The largest number of MCU rows transformed at once (<see cref="GetBatchRows"/>).</param>
    /// <param name="cancellationToken">Checked before each MCU row and each band.</param>
    public JpegReconstruction(JpegFrameComponent[] components, JpegBandWriter[] writers, ushort[][] quantization, int mcuRows, int batchRows, CancellationToken cancellationToken)
    {
        _components = components;
        _writers = writers;
        _quantization = quantization;
        _mcuRows = mcuRows;
        BatchRows = batchRows;
        _cancellationToken = cancellationToken;
        _blocks = new double[writers.Length][];
        _scales = new double[writers.Length][];
        for (var i = 0; i < writers.Length; i++)
        {
            _blocks[i] = new double[64];
            _scales[i] = new double[64];
        }
    }

    /// <summary>Gets the number of MCU rows whose samples are in the planes.</summary>
    public int TransformedRows { get; private set; }

    /// <summary>Gets the largest number of MCU rows transformed at once: the rows of the coefficient ring of a sequential frame.</summary>
    public int BatchRows { get; }

    /// <summary>Gets the number of workers of a frame: several for a frame of at least <see cref="RowBands.MinPixelsForParallelism"/> pixels.</summary>
    public static int GetWorkerCount(Size frameSize, int mcuRows, int maxDegreeOfParallelism)
        => Math.Min(RowBands.GetWorkerCount(frameSize, maxDegreeOfParallelism), mcuRows);

    /// <summary>Gets the number of MCU rows of the coefficient ring of a sequential frame: the rows of a batch.</summary>
    public static int GetBatchRows(int workers, int mcuRows) => Math.Min(mcuRows, workers * RowsPerWorker);

    /// <summary>Gets the number of bands of the planes: the rows of a batch and the two rows before them.</summary>
    public static int GetPlaneBands(int batchRows, int mcuRows) => Math.Min(mcuRows, batchRows + 2);

    /// <summary>
    /// Transforms the MCU rows from <see cref="TransformedRows"/> to <paramref name="endRow"/> (excluded, at most the rows
    /// of the planes but two), then writes every band that has its lower context row: up to the band before
    /// <paramref name="endRow"/>, or every band once the last row is transformed.
    /// </summary>
    public void Reconstruct(int endRow)
    {
        var first = TransformedRows;
        var count = endRow - first;
        Debug.Assert(count <= BatchRows);
        if (count > 0)
        {
            var workers = Math.Min(_writers.Length, count);
            RowBands.RunParallelInBandOrder(workers, worker =>
            {
                var block = _blocks[worker];
                var scales = _scales[worker];
                var end = first + RowBands.GetBandStart(count, worker + 1, workers);
                for (var row = first + RowBands.GetBandStart(count, worker, workers); row < end; row++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    foreach (var component in _components)
                    {
                        TransformMcuRow(component, row, _quantization[component.Index], block, scales);
                    }
                }
            });

            TransformedRows = endRow;
        }

        JpegBandWriter.WriteBands(_writers, endRow >= _mcuRows ? _mcuRows : endRow - 1, _cancellationToken);
    }

    /// <summary>
    /// Dequantizes and transforms the blocks of one MCU row of a component into its sample plane: the coefficients of block
    /// row <c>r</c> are in row <c>r % CoefficientBlockRows</c> of the coefficient grid.
    /// </summary>
    /// <param name="component">The component.</param>
    /// <param name="mcuRow">The MCU row.</param>
    /// <param name="quantization">The quantization table, in natural order.</param>
    /// <param name="block">A scratch block of 64 values.</param>
    /// <param name="scales">A scratch block of 64 values, which receives the quantization values.</param>
    public static void TransformMcuRow(JpegFrameComponent component, int mcuRow, ushort[] quantization, double[] block, double[] scales)
    {
        for (var i = 0; i < 64; i++)
        {
            scales[i] = quantization[i];
        }

        var stride = component.Stride;
        var precision = component.Precision;
        for (var blockRow = mcuRow * component.VerticalSampling; blockRow < (mcuRow + 1) * component.VerticalSampling; blockRow++)
        {
            var rowOffset = component.GetRowOffset(blockRow * 8);
            var coefficientRow = blockRow % component.CoefficientBlockRows;
            for (var blockColumn = 0; blockColumn < component.CoefficientBlocksPerLine; blockColumn++)
            {
                var coefficients = component.GetCoefficients(coefficientRow, blockColumn);
                var offset = rowOffset + (blockColumn * 8);
                if (coefficients[1..].IndexOfAnyExcept((short)0) < 0)
                {
                    var dc = (long)coefficients[0] * quantization[0];
                    if (precision > 8)
                    {
                        JpegIdct.TransformDeepDcOnly(dc, component.DeepSamples[offset..], stride, precision);
                    }
                    else
                    {
                        JpegIdct.TransformDcOnly(dc, component.Samples.AsSpan(offset), stride);
                    }

                    continue;
                }

                JpegIdct.Dequantize(coefficients, scales, block);

                if (precision > 8)
                {
                    JpegIdct.TransformDeep(block, component.DeepSamples[offset..], stride, precision);
                }
                else
                {
                    JpegIdct.Transform(block, component.Samples.AsSpan(offset), stride, JpegIdct.IsSparse(coefficients));
                }
            }
        }
    }
}
