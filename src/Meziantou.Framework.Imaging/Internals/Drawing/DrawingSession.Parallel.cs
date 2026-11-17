using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>How the rows of a large shape are accumulated and composited by several workers.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> above 1, the rows of a shape that
/// may cover at least <see cref="MinPixelsForParallelPaint"/> pixels are composited by up to that many workers, the
/// calling thread included, and the pixels are those of one worker: each row is composited once, with the same paint and
/// the same formulas, and the rows do not depend on one another. The sweep of the rasterizer carries its state from one
/// row to the next, so it stays on the calling thread; it fills batches of about <see cref="PixelsPerBatch"/> pixels,
/// which the workers composite while it sweeps the next rows.
/// </para>
/// <para>
/// <b>Rasterization.</b> With antialiasing, the sweep only finds the pieces of boundary of each row
/// (<see cref="CoverageRasterizer.RecordPieces"/>): the workers accumulate them in their own difference row, in the
/// order the sweep found them, and turn it into coverages (<see cref="CoverageRasterizer.ReplayPieces"/>), which takes
/// time in proportion to the width of the rows. The coverages are those of a sweep on one thread, bit for bit: the sums
/// are made of the same terms, added in the same order. The rows with more than
/// <see cref="CoverageRasterizer.MaxPiecesPerRow"/> pieces, the rows without antialiasing and the rectangles of whole
/// pixels come as coverages, which the sweep copies to the batch; a paint that gives the pixels covered entirely the same
/// samples, whatever they were (<see cref="IsWrittenFasterThanCopied"/>), then stays on the calling thread: copying the
/// coverages to a batch costs about what writing those pixels costs.
/// </para>
/// <para>
/// Each worker has its own scratch, rented before any pixel changes: the coverages and the pieces of boundary of its
/// batch, a difference row and a row of coverages for the pieces, the products of the coverages with the clip and the
/// opacity, the colors with the opacity of the brush, and a copy of the paint
/// (<see cref="PaintEvaluator.CreateWorker"/>). A paint that has no copy for another thread (a gradient mixed in a paint
/// profile) is composited on the calling thread, and so are the texts, the masks of the clips, the color glyphs, the
/// blurs, the shadows and the filters, which do not go through <see cref="PaintRows"/>.
/// </para>
/// <para>
/// <b>Failures.</b> A batch that fails stops the sweep at the next batch, and the exception is the one of the earliest
/// row that failed, or else the one of the sweep (a cancellation): the one of a sequential run. Rows after the one that
/// failed may be composited, as some rows are when a draw call is canceled.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    /// <summary>The number of covered pixels from which a batch is handed to a worker.</summary>
    internal const int PixelsPerBatch = 32 * 1024;

    /// <summary>The smallest bound of the covered pixels of a shape from which its runs are composited by several workers.</summary>
    internal const long MinPixelsForParallelPaint = 4L * PixelsPerBatch;

    /// <summary>The largest number of runs of a batch, which bounds the work of the sweep between two hand-offs for narrow shapes.</summary>
    private const int MaxRunsPerBatch = 1024;

    /// <summary>The number of pieces of boundary a batch holds: a batch is handed off before a row may not fit.</summary>
    private const int MaxPiecesPerBatch = 8 * CoverageRasterizer.MaxPiecesPerRow;

    /// <summary>The number of values of a run of a batch: its row, its first column (or -1 for pieces of boundary), and the offset and length of its values.</summary>
    private const int ValuesPerRun = 4;

    // The scratch of the workers of the largest number of workers that composited a shape; kept until the session ends
    private PaintWorker[]? _paintWorkers;

    private static readonly AsyncLocal<Action<int, bool>?> TestBatchObserverValue = new();
    private static readonly AsyncLocal<bool> TestDisablePiecesValue = new();

    /// <summary>
    /// Gets or sets a value indicating whether the workers are given coverages only, as before the sweep gave them pieces of
    /// boundary: a test hook that compares both ways of handing the rows to the workers.
    /// </summary>
    internal static bool TestDisablePieces
    {
        get => TestDisablePiecesValue.Value;
        set => TestDisablePiecesValue.Value = value;
    }

    /// <summary>
    /// Gets or sets a test hook called on the thread that composites a batch, when it starts (<see langword="true"/>) and
    /// ends, with the number of workers of the shape.
    /// </summary>
    internal static Action<int, bool>? TestBatchObserver
    {
        get => TestBatchObserverValue.Value;
        set => TestBatchObserverValue.Value = value;
    }

    /// <summary>Composites the runs of the sweep with several workers, when the shape is large enough and the paint allows it.</summary>
    /// <returns><see langword="false"/> when the runs are to be composited on the calling thread: nothing was done.</returns>
    private bool TryPaintRowsInParallel(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, BlendMode blend, uint opacity, double alphaFactor)
    {
        if (!TryGetParallelPaint(color, evaluator, mode, blend, opacity, alphaFactor, out var workerCount, out var needsCoverageRow, out var needsAlphaRow, out var recordsPieces))
            return false;

        if (!PreparePaintWorkers(workerCount, evaluator, needsCoverageRow, needsAlphaRow, recordsPieces))
            return false;

        PaintRowsInParallel(new PaintParameters(color, mode, blend, opacity, alphaFactor, workerCount, _rasterizer!.IsFullyCovered, TestBatchObserver));
        return true;
    }

    /// <summary>
    /// Rents the scratch of the workers that <see cref="TryPaintRowsInParallel"/> would use for the shape that was prepared,
    /// without painting it: a call that draws several shapes (the parts of a text) prepares them all before its first
    /// pixel write.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    private void ReservePaintWorkers(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, BlendMode blend, double alphaFactor)
    {
        if (!TryGetParallelPaint(color, evaluator, mode, blend, CoverageRasterizer.FullCoverage, alphaFactor, out var workerCount, out var needsCoverageRow, out var needsAlphaRow, out var recordsPieces))
            return;

        var paintWorkers = EnsurePaintWorkers(workerCount);
        for (var i = 0; i < workerCount; i++)
        {
            (paintWorkers[i] ??= new PaintWorker()).EnsureScratch(_image.Owner.Scope, Width, needsCoverageRow, needsAlphaRow, recordsPieces);
        }

        if (recordsPieces)
        {
            _rasterizer!.ReservePieces();
        }
    }

    /// <summary>
    /// Gets whether the shape that was prepared is composited by several workers, and what their scratch is: a paint that
    /// has no copy for other threads is composited on the calling thread, and its shape rents no scratch for workers.
    /// </summary>
    private bool TryGetParallelPaint(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, BlendMode blend, uint opacity, double alphaFactor, out int workerCount, out bool needsCoverageRow, out bool needsAlphaRow, out bool recordsPieces)
    {
        var rasterizer = _rasterizer!;
        workerCount = evaluator is null || evaluator.CanCreateWorkers ? GetPaintWorkerCount(_image.Configuration.MaxDegreeOfParallelism, rasterizer.EstimatedPixels) : 1;
        needsCoverageRow = _clipDepth != 0 || opacity != CoverageRasterizer.FullCoverage;
        needsAlphaRow = evaluator is not null && alphaFactor != 1;

        // The rows that come as pieces of boundary are not copied: the workers compute their coverages
        recordsPieces = rasterizer.CanRecordPieces && !TestDisablePieces;
        if (workerCount <= 1)
            return false;

        return recordsPieces || !IsWrittenFasterThanCopied(color, evaluator, mode, blend, opacity);
    }

    /// <summary>
    /// Gets a value indicating whether a paint gives the pixels covered entirely the same samples, whatever they were (an
    /// opaque color, or a color that replaces the pixels): a worker would write most of its pixels as fast as the calling
    /// thread copies their coverages to its batch, so the workers would only add their cost.
    /// </summary>
    private bool IsWrittenFasterThanCopied(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, BlendMode blend, uint opacity)
    {
        if (evaluator is not null || opacity != CoverageRasterizer.FullCoverage || _clipDepth != 0)
            return false;

        var source = new DrawingSource(color, mode, blend, _transfer, _curves);
        for (var i = 0; i < _frames.Length; i++)
        {
            if (!_frames[i].IsUniformOverFullCoverage(source, Width))
                return false;
        }

        return true;
    }

    /// <summary>Hands the batches of the sweep to the workers that <see cref="PreparePaintWorkers"/> prepared, and waits for them.</summary>
    /// <remarks>A method of its own: the closure of the workers is only allocated when they run.</remarks>
    private void PaintRowsInParallel(PaintParameters paint)
    {
        var rasterizer = _rasterizer!;
        var workerCount = paint.WorkerCount;
        var workers = _paintWorkers!;
        var sequence = 0;
        Exception? sweepFailure = null;
        var current = workers[0];
        current.Start(sequence++);
        try
        {
            while (true)
            {
                bool hasRun;
                try
                {
                    hasRun = rasterizer.MoveNext();
                }
                catch (Exception exception)
                {
                    sweepFailure = exception;
                    break;
                }

                if (!hasRun)
                {
                    // The last batch is composited by the calling thread, which has nothing else to do
                    if (current.RunCount > 0)
                    {
                        RunBatch(current, paint);
                    }

                    break;
                }

                if (rasterizer.HasPieces)
                {
                    current.AddPieces(rasterizer.Y, rasterizer.Pieces, rasterizer.PieceColumns);
                }
                else
                {
                    current.Add(rasterizer.Y, rasterizer.X, rasterizer.Coverage);
                }

                if (!current.IsFull)
                    continue;

                // Up to one worker per batch beside the calling thread, which composites the batch itself when they are all busy
                if (CountBusyWorkers(workers, workerCount) < workerCount - 1)
                {
                    var batch = current;
                    batch.Task = Task.Run(() => RunBatch(batch, paint));
                }
                else
                {
                    RunBatch(current, paint);
                }

                if (HasFailedBatch(workers, workerCount))
                    break;

                current = TakeIdleWorker(workers, workerCount);
                current.Start(sequence++);
            }
        }
        finally
        {
            for (var i = 0; i < workerCount; i++)
            {
                workers[i].Wait();
            }

            for (var i = 0; i < workerCount; i++)
            {
                var worker = workers[i];
                worker.ReleaseEvaluator();
                if (worker.HasDrawn)
                {
                    MarkDrawn(worker.DrawnLeft, worker.DrawnTop, worker.DrawnRight, worker.DrawnBottom);
                }
            }
        }

        Exception? failure = null;
        var failedSequence = int.MaxValue;
        for (var i = 0; i < workerCount; i++)
        {
            if (workers[i].Failure is { } exception && workers[i].FailedSequence < failedSequence)
            {
                failure = exception;
                failedSequence = workers[i].FailedSequence;
            }
        }

        failure ??= sweepFailure;
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    /// <summary>Gets the number of workers that composite a shape: one below <see cref="MinPixelsForParallelPaint"/> pixels, and at most one per batch.</summary>
    internal static int GetPaintWorkerCount(int maxDegreeOfParallelism, long estimatedPixels)
    {
        if (maxDegreeOfParallelism <= 1 || estimatedPixels < MinPixelsForParallelPaint)
            return 1;

        return (int)Math.Min(maxDegreeOfParallelism, estimatedPixels / PixelsPerBatch);
    }

    /// <summary>Rents the scratch of the workers and the copies of the paint: every rental of the parallel composition happens here.</summary>
    /// <returns><see langword="false"/> when the paint cannot be evaluated by other threads.</returns>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    private bool PreparePaintWorkers(int workerCount, PaintEvaluator? evaluator, bool needsCoverageRow, bool needsAlphaRow, bool recordsPieces)
    {
        var scope = _image.Owner.Scope;
        var paintWorkers = EnsurePaintWorkers(workerCount);
        try
        {
            for (var i = 0; i < workerCount; i++)
            {
                var worker = paintWorkers[i] ??= new PaintWorker();
                worker.EnsureScratch(scope, Width, needsCoverageRow, needsAlphaRow, recordsPieces);
                worker.ResetDrawn();
                if (evaluator is not null)
                {
                    // A paint without copies for other threads has none from the first one
                    var copy = evaluator.CreateWorker();
                    if (copy is null)
                    {
                        ReleasePaintEvaluators(workerCount);
                        return false;
                    }

                    worker.Evaluator = copy;
                }
            }

            // A shape whose rows come as pieces is composited by the workers
            if (recordsPieces)
            {
                _rasterizer!.RecordPieces();
            }

            if (evaluator is not null)
            {
                // The scratch that only saves time comes last: it never takes the room of the scratch the call needs
                for (var i = 0; i < workerCount; i++)
                {
                    paintWorkers[i].Evaluator!.PrepareOptionalScratch();
                }
            }
        }
        catch
        {
            ReleasePaintEvaluators(workerCount);
            throw;
        }

        return true;
    }

    /// <summary>Gets the workers, with room for at least a number of them: the workers already created are kept.</summary>
    private PaintWorker[] EnsurePaintWorkers(int workerCount)
    {
        if (_paintWorkers is null || _paintWorkers.Length < workerCount)
        {
            var workers = new PaintWorker[workerCount];
            _paintWorkers?.CopyTo(workers, 0);
            _paintWorkers = workers;
        }

        return _paintWorkers;
    }

    private void ReleasePaintEvaluators(int workerCount)
    {
        for (var i = 0; i < workerCount; i++)
        {
            _paintWorkers![i]?.ReleaseEvaluator();
        }
    }

    /// <summary>Composites the runs of a batch, in order, and records the failure of the first one that fails.</summary>
    private void RunBatch(PaintWorker worker, PaintParameters paint)
    {
        paint.Observer?.Invoke(paint.WorkerCount, true);
        try
        {
            var coverage = worker.CoverageSpan;
            var runs = worker.RunSpan;
            for (var i = 0; i < worker.RunCount; i++)
            {
                var y = runs[ValuesPerRun * i];
                var x = runs[(ValuesPerRun * i) + 1];
                var offset = runs[(ValuesPerRun * i) + 2];
                var length = runs[(ValuesPerRun * i) + 3];
                if (x >= 0)
                {
                    PaintSpan(worker, paint.Color, worker.Evaluator, paint.Mode, paint.Blend, paint.Opacity, y, x, coverage.Slice(offset, length), paint.AlphaFactor, paint.IsFullyCovered);
                    continue;
                }

                var rowCoverage = worker.RowCoverageSpan;
                if (CoverageRasterizer.ReplayPieces(worker.PieceSpan.Slice(offset, length), worker.AccumulationSpan, rowCoverage, Width, out x, out var count))
                {
                    PaintSpan(worker, paint.Color, worker.Evaluator, paint.Mode, paint.Blend, paint.Opacity, y, x, rowCoverage.Slice(x, count), paint.AlphaFactor);
                }
            }
        }
        catch (Exception exception)
        {
            // The difference row is all zeros for the next batch
            worker.ClearAccumulation();
            worker.Fail(exception);
        }
        finally
        {
            paint.Observer?.Invoke(paint.WorkerCount, false);
        }
    }

    private static int CountBusyWorkers(PaintWorker[] workers, int workerCount)
    {
        var count = 0;
        for (var i = 0; i < workerCount; i++)
        {
            if (workers[i].Task is { IsCompleted: false })
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasFailedBatch(PaintWorker[] workers, int workerCount)
    {
        for (var i = 0; i < workerCount; i++)
        {
            var worker = workers[i];
            if (worker.Task is null or { IsCompleted: true } && worker.Failure is not null)
                return true;
        }

        return false;
    }

    /// <summary>Gets a worker that composites nothing: there is always one, since at most all but one composite in the background.</summary>
    private static PaintWorker TakeIdleWorker(PaintWorker[] workers, int workerCount)
    {
        for (var i = 0; i < workerCount; i++)
        {
            var worker = workers[i];
            if (worker.Task is null)
                return worker;
        }

        for (var i = 0; i < workerCount; i++)
        {
            var worker = workers[i];
            if (worker.Task!.IsCompleted)
            {
                worker.Wait();
                return worker;
            }
        }

        // Unreachable: the batch that was just handed off left at least one worker idle or composited on this thread
        workers[0].Wait();
        return workers[0];
    }

    private void DisposePaintWorkers()
    {
        if (_paintWorkers is null)
            return;

        foreach (var worker in _paintWorkers)
        {
            worker?.Dispose();
        }

        _paintWorkers = null;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PaintParameters(DrawingColor Color, CompositeMode Mode, BlendMode Blend, uint Opacity, double AlphaFactor, int WorkerCount, bool IsFullyCovered, Action<int, bool>? Observer);

    /// <summary>The scratch of a worker, and the batch of runs it composites.</summary>
    private sealed class PaintWorker : IDisposable
    {
        private PooledBuffer? _coverage;
        private PooledBuffer? _runs;
        private PooledBuffer? _pieces;
        private PooledBuffer? _accumulation;
        private PooledBuffer? _rowCoverage;
        private int _width;
        private int _sequence;

        /// <summary>Gets the row the products of the coverages are written to, when the shape is clipped or has an opacity.</summary>
        public PooledBuffer? CoverageRow { get; private set; }

        /// <summary>Gets the row of the colors whose alpha is multiplied by the opacity of the brush.</summary>
        public PooledBuffer? AlphaRow { get; private set; }

        /// <summary>Gets or sets the copy of the paint of the shape.</summary>
        public PaintEvaluator? Evaluator { get; set; }

        /// <summary>Gets or sets the task that composites the batch, or <see langword="null"/> when the worker is idle or the calling thread composited it.</summary>
        public Task? Task { get; set; }

        public int RunCount { get; private set; }

        /// <summary>Gets the number of pixels of the batch: the coverages it holds, and the columns its pieces of boundary go through.</summary>
        public int PixelCount { get; private set; }

        /// <summary>Gets the number of coverages of the batch.</summary>
        public int CoverageCount { get; private set; }

        /// <summary>Gets the number of values of the pieces of boundary of the batch.</summary>
        public int PieceValueCount { get; private set; }

        /// <summary>Gets a value indicating whether the batch is handed off: enough pixels, enough runs, or not enough room for the pieces of one more row.</summary>
        public bool IsFull => PixelCount >= PixelsPerBatch || RunCount == MaxRunsPerBatch || PieceValueCount > (MaxPiecesPerBatch - CoverageRasterizer.MaxPiecesPerRow) * CoverageRasterizer.ValuesPerPiece;

        public Exception? Failure { get; private set; }

        /// <summary>Gets the sequence number of the batch that failed: the batches follow the rows.</summary>
        public int FailedSequence { get; private set; }

        public bool HasDrawn => DrawnRight > DrawnLeft;

        public int DrawnLeft { get; private set; }

        public int DrawnTop { get; private set; }

        public int DrawnRight { get; private set; }

        public int DrawnBottom { get; private set; }

        /// <summary>The coverages of the runs, one after the other.</summary>
        public Span<uint> CoverageSpan => unsafe(MemoryMarshal.Cast<byte, uint>(_coverage!.Span));

        /// <summary>For each run: its row, its first column (or -1 for pieces of boundary), and the offset and length of its coverages or of its pieces.</summary>
        public Span<int> RunSpan => unsafe(MemoryMarshal.Cast<byte, int>(_runs!.Span));

        /// <summary>The pieces of boundary of the runs, one after the other.</summary>
        public Span<double> PieceSpan => unsafe(MemoryMarshal.Cast<byte, double>(_pieces!.Span));

        /// <summary>The difference row the pieces of a row are accumulated in: all zeros between two rows.</summary>
        public Span<double> AccumulationSpan => unsafe(MemoryMarshal.Cast<byte, double>(_accumulation!.Span));

        /// <summary>The coverages of the row whose pieces were accumulated last.</summary>
        public Span<uint> RowCoverageSpan => unsafe(MemoryMarshal.Cast<byte, uint>(_rowCoverage!.Span))[.._width];

        /// <summary>Rents what the worker needs for a canvas of a width: what it already has is kept.</summary>
        /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
        public void EnsureScratch(AllocationScope scope, int width, bool needsCoverageRow, bool needsAlphaRow, bool needsPieces)
        {
            if (_width != width)
            {
                _coverage?.Dispose();
                CoverageRow?.Dispose();
                AlphaRow?.Dispose();
                _accumulation?.Dispose();
                _rowCoverage?.Dispose();
                _coverage = null;
                CoverageRow = null;
                AlphaRow = null;
                _accumulation = null;
                _rowCoverage = null;
                _width = width;
            }

            // A batch is handed off once it has PixelsPerBatch pixels: one more run of at most a row always fits
            _coverage ??= ResampleWeights.Rent<uint>(scope, (long)PixelsPerBatch + width);
            _runs ??= ResampleWeights.Rent<int>(scope, (long)ValuesPerRun * MaxRunsPerBatch);
            if (needsPieces)
            {
                // The difference row has the two entries past the last column that the sweep's has
                _pieces ??= ResampleWeights.Rent<double>(scope, (long)MaxPiecesPerBatch * CoverageRasterizer.ValuesPerPiece);
                _accumulation ??= ResampleWeights.Rent<double>(scope, (long)width + 2);
                _rowCoverage ??= ResampleWeights.Rent<uint>(scope, width);
            }
            if (needsCoverageRow)
            {
                CoverageRow ??= ResampleWeights.Rent<uint>(scope, width);
            }

            if (needsAlphaRow)
            {
                AlphaRow ??= ResampleWeights.Rent<ushort>(scope, 4L * width);
            }
        }

        public void ResetDrawn()
        {
            DrawnLeft = DrawnTop = DrawnRight = DrawnBottom = 0;
            Failure = null;
            FailedSequence = int.MaxValue;
            Task = null;
        }

        /// <summary>Starts a new batch.</summary>
        public void Start(int sequence)
        {
            _sequence = sequence;
            RunCount = 0;
            PixelCount = 0;
            CoverageCount = 0;
            PieceValueCount = 0;
        }

        /// <summary>Adds a run of coverages to the batch.</summary>
        public void Add(int y, int x, ReadOnlySpan<uint> coverage)
        {
            AddRun(y, x, CoverageCount, coverage.Length);
            coverage.CopyTo(CoverageSpan[CoverageCount..]);
            CoverageCount += coverage.Length;
            PixelCount += coverage.Length;
        }

        /// <summary>Adds the pieces of boundary of a row to the batch, which go through about <paramref name="columns"/> columns.</summary>
        public void AddPieces(int y, ReadOnlySpan<double> pieces, int columns)
        {
            AddRun(y, -1, PieceValueCount, pieces.Length);
            pieces.CopyTo(PieceSpan[PieceValueCount..]);
            PieceValueCount += pieces.Length;
            PixelCount += columns;
        }

        private void AddRun(int y, int x, int offset, int length)
        {
            var runs = RunSpan[(ValuesPerRun * RunCount)..];
            runs[0] = y;
            runs[1] = x;
            runs[2] = offset;
            runs[3] = length;
            RunCount++;
        }

        /// <summary>Clears the difference row, which a failure may leave with entries that are not zero.</summary>
        public void ClearAccumulation() => _accumulation?.Span.Clear();

        public void MarkDrawn(int left, int top, int right, int bottom)
        {
            if (right <= left || bottom <= top)
                return;

            if (!HasDrawn)
            {
                (DrawnLeft, DrawnTop, DrawnRight, DrawnBottom) = (left, top, right, bottom);
                return;
            }

            DrawnLeft = Math.Min(DrawnLeft, left);
            DrawnTop = Math.Min(DrawnTop, top);
            DrawnRight = Math.Max(DrawnRight, right);
            DrawnBottom = Math.Max(DrawnBottom, bottom);
        }

        /// <summary>Records the failure of the batch, unless an earlier batch of the worker failed.</summary>
        public void Fail(Exception exception)
        {
            if (_sequence < FailedSequence)
            {
                Failure = exception;
                FailedSequence = _sequence;
            }
        }

        /// <summary>Waits for the batch: its failure, if any, is recorded by <see cref="Fail"/>.</summary>
        public void Wait()
        {
            if (Task is { } task)
            {
                // The task records its failures itself: it never faults
                task.Wait();
                Task = null;
            }
        }

        public void ReleaseEvaluator()
        {
            Evaluator?.ReleaseWorker();
            Evaluator = null;
        }

        public void Dispose()
        {
            Wait();
            ReleaseEvaluator();
            _coverage?.Dispose();
            _runs?.Dispose();
            _pieces?.Dispose();
            _accumulation?.Dispose();
            _rowCoverage?.Dispose();
            CoverageRow?.Dispose();
            AlphaRow?.Dispose();
            _coverage = null;
            _runs = null;
            _pieces = null;
            _accumulation = null;
            _rowCoverage = null;
            CoverageRow = null;
            AlphaRow = null;
            _width = 0;
        }
    }
}
