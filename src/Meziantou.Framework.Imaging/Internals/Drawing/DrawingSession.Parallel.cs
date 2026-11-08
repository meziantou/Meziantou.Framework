using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>How the runs of a large shape are composited by several workers.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> above 1, the runs of a shape whose rows
/// may cover at least <see cref="MinPixelsForParallelPaint"/> pixels are composited by up to that many workers, the
/// calling thread included, and the pixels are those of one worker: each row is composited once, with the same paint and
/// the same formulas, and the rows do not depend on one another. The sweep of the rasterizer carries its state from one
/// row to the next, so it stays on the calling thread; it fills batches of about <see cref="PixelsPerBatch"/> covered
/// pixels, which the workers composite while it sweeps the next rows.
/// </para>
/// <para>
/// Each worker has its own scratch, rented before any pixel changes: the coverages of its batch, the products of the
/// coverages with the clip and the opacity, the colors with the opacity of the brush, and a copy of the paint
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

    // The scratch of the workers of the largest number of workers that composited a shape; kept until the session ends
    private PaintWorker[]? _paintWorkers;

    private static readonly AsyncLocal<Action<int, bool>?> TestBatchObserverValue = new();

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
        var rasterizer = _rasterizer!;
        var workerCount = GetPaintWorkerCount(_image.Configuration.MaxDegreeOfParallelism, rasterizer.EstimatedPixels);
        if (workerCount <= 1)
            return false;

        var needsCoverageRow = _clipDepth != 0 || opacity != CoverageRasterizer.FullCoverage;
        var needsAlphaRow = evaluator is not null && alphaFactor != 1;
        if (!PreparePaintWorkers(workerCount, evaluator, needsCoverageRow, needsAlphaRow))
            return false;

        PaintRowsInParallel(new PaintParameters(color, mode, blend, opacity, alphaFactor, workerCount, TestBatchObserver));
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

                current.Add(rasterizer.Y, rasterizer.X, rasterizer.Coverage);
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
    private bool PreparePaintWorkers(int workerCount, PaintEvaluator? evaluator, bool needsCoverageRow, bool needsAlphaRow)
    {
        var scope = _image.Owner.Scope;
        if (_paintWorkers is null || _paintWorkers.Length < workerCount)
        {
            var previous = _paintWorkers;
            var workers = new PaintWorker[workerCount];
            previous?.CopyTo(workers, 0);
            _paintWorkers = workers;
        }

        var paintWorkers = _paintWorkers;
        try
        {
            for (var i = 0; i < workerCount; i++)
            {
                var worker = paintWorkers[i] ??= new PaintWorker();
                worker.EnsureScratch(scope, Width, needsCoverageRow, needsAlphaRow);
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
        }
        catch
        {
            ReleasePaintEvaluators(workerCount);
            throw;
        }

        return true;
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
                var y = runs[3 * i];
                var x = runs[(3 * i) + 1];
                var offset = runs[(3 * i) + 2];
                var end = i + 1 < worker.RunCount ? runs[(3 * (i + 1)) + 2] : worker.PixelCount;
                PaintSpan(worker, paint.Color, worker.Evaluator, paint.Mode, paint.Blend, paint.Opacity, y, x, coverage[offset..end], paint.AlphaFactor);
            }
        }
        catch (Exception exception)
        {
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
    private readonly record struct PaintParameters(DrawingColor Color, CompositeMode Mode, BlendMode Blend, uint Opacity, double AlphaFactor, int WorkerCount, Action<int, bool>? Observer);

    /// <summary>The scratch of a worker, and the batch of runs it composites.</summary>
    private sealed class PaintWorker : IDisposable
    {
        private PooledBuffer? _coverage;
        private PooledBuffer? _runs;
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

        public int PixelCount { get; private set; }

        /// <summary>Gets a value indicating whether the batch is handed off: enough pixels, or enough runs.</summary>
        public bool IsFull => PixelCount >= PixelsPerBatch || RunCount == MaxRunsPerBatch;

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

        /// <summary>For each run: its row, its first column and the offset of its coverages.</summary>
        public Span<int> RunSpan => unsafe(MemoryMarshal.Cast<byte, int>(_runs!.Span));

        /// <summary>Rents what the worker needs for a canvas of a width: what it already has is kept.</summary>
        /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
        public void EnsureScratch(AllocationScope scope, int width, bool needsCoverageRow, bool needsAlphaRow)
        {
            if (_width != width)
            {
                _coverage?.Dispose();
                CoverageRow?.Dispose();
                AlphaRow?.Dispose();
                _coverage = null;
                CoverageRow = null;
                AlphaRow = null;
                _width = width;
            }

            // A batch is handed off once it has PixelsPerBatch pixels: one more run of at most a row always fits
            _coverage ??= ResampleWeights.Rent<uint>(scope, (long)PixelsPerBatch + width);
            _runs ??= ResampleWeights.Rent<int>(scope, 3L * MaxRunsPerBatch);
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
        }

        /// <summary>Adds a run to the batch.</summary>
        public void Add(int y, int x, ReadOnlySpan<uint> coverage)
        {
            var runs = RunSpan;
            runs[3 * RunCount] = y;
            runs[(3 * RunCount) + 1] = x;
            runs[(3 * RunCount) + 2] = PixelCount;
            coverage.CopyTo(CoverageSpan[PixelCount..]);
            PixelCount += coverage.Length;
            RunCount++;
        }

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
            CoverageRow?.Dispose();
            AlphaRow?.Dispose();
            _coverage = null;
            _runs = null;
            CoverageRow = null;
            AlphaRow = null;
            _width = 0;
        }
    }
}
