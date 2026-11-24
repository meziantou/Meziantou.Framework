using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Meziantou.Framework.Csv.Internals;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Serialization.Internals;

/// <summary>Reads the records on the calling thread and converts them to objects on the thread pool, in batches, in the order of the records.</summary>
/// <remarks>
/// <para>
/// Splitting the data into records is a small part of the time, and it cannot start in the middle of the data: whether a line terminator ends a record
/// depends on the quotes before it. The parser reads the records on the calling thread; each accepted record is added to a batch (copied, unless the data is in memory), and a task converts
/// the records of the batch. The batches are returned in the order of the records, so the objects and the exceptions are the same as when reading the
/// records one by one: an error of the parser is thrown once the records before it are returned.
/// </para>
/// <para>
/// At most <c>maxPendingBatches</c> batches are converted or waiting to be returned: the parser does not read ahead of the consumer by more than that,
/// which bounds the memory when reading a stream.
/// </para>
/// </remarks>
internal sealed class CsvParallelDeserializer<T, TChar> : IDisposable, IAsyncDisposable
    where TChar : unmanaged, IBinaryInteger<TChar>
{
    // A batch is a few hundred records: enough for the cost of a task to be negligible, few enough for the first objects to come quickly
    public const int DefaultMaxBatchRows = 256;
    private const int MaxBatchLength = 64 * 1024;

    private readonly CsvTypeInfo<T> _typeInfo;
    private readonly int _maxPendingBatches;
    private readonly int _maxBatchRows;
    private readonly Queue<Batch> _pending = new();
    private readonly Stack<Batch> _free = new();
    private readonly List<Batch> _batches = [];
    private readonly Lock _errorHandlerLock = new();
    private CsvRecordBinder<T>? _binder;
    private CsvDeserializationErrorHandler? _errorHandler;
    private Batch? _filling;
    private Batch? _current;
    private bool _endOfRecords;
    private ExceptionDispatchInfo? _readError;
    private ExceptionDispatchInfo? _conversionError;

    public CsvParallelDeserializer(CsvTypeInfo<T> typeInfo, int maxPendingBatches, int maxBatchRows = DefaultMaxBatchRows)
    {
        _typeInfo = typeInfo;
        _maxPendingBatches = maxPendingBatches;
        _maxBatchRows = maxBatchRows;
    }

    /// <summary>Gets the objects of the current batch.</summary>
    public ArraySegment<T> Current => _current?.Results ?? default;

    /// <summary>Waits for the next batch of objects.</summary>
    /// <returns><see langword="false"/> when every record is read.</returns>
    public bool MoveNext(CsvParser<TChar> parser)
    {
        ReleaseCurrent();
        _conversionError?.Throw();
        if (!_endOfRecords)
        {
            try
            {
                while (_pending.Count < _maxPendingBatches)
                {
                    if (!parser.Read())
                    {
                        EndOfRecords(error: null);
                        break;
                    }

                    Add(parser);
                }
            }
            catch (Exception ex)
            {
                EndOfRecords(ex);
            }
        }

        if (!_pending.TryDequeue(out var batch))
        {
            _readError?.Throw();
            return false;
        }

        _current = batch;
        if (!batch.TryConvert())
        {
            batch.Task!.GetAwaiter().GetResult();
        }

        return OnBatchCompleted(batch);
    }

    /// <summary>Waits for the next batch of objects, reading the data asynchronously.</summary>
    /// <returns><see langword="false"/> when every record is read.</returns>
    public async ValueTask<bool> MoveNextAsync(CsvParser<TChar> parser, CancellationToken cancellationToken)
    {
        ReleaseCurrent();
        _conversionError?.Throw();

        // Reading the records one by one checks the token before reading each record, after the error of the previous one
        cancellationToken.ThrowIfCancellationRequested();
        if (!_endOfRecords)
        {
            try
            {
                while (_pending.Count < _maxPendingBatches)
                {
                    if (!await parser.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        EndOfRecords(error: null);
                        break;
                    }

                    Add(parser);
                }
            }
            catch (Exception ex)
            {
                EndOfRecords(ex);
            }
        }

        if (!_pending.TryDequeue(out var batch))
        {
            _readError?.Throw();
            return false;
        }

        _current = batch;
        if (!batch.TryConvert())
        {
            await batch.Task!.ConfigureAwait(false);
        }

        return OnBatchCompleted(batch);
    }

    // The objects created before an error are returned, then the error is thrown: the later batches are not returned
    private bool OnBatchCompleted(Batch batch)
    {
        if (batch.Error is { } error)
        {
            _endOfRecords = true;
            _conversionError = error;
            if (batch.Results.Count == 0)
            {
                error.Throw();
            }
        }

        return true;
    }

    private void Add(CsvParser<TChar> parser)
    {
        var batch = _filling;
        if (batch is null)
        {
            if (_binder is null)
            {
                // The errors of the header row are reported here, on the calling thread
                _binder = CsvRecordBinder<T>.Create(_typeInfo, parser);
                if (_binder.ErrorHandler is { } handler)
                {
                    // The handler is called from the tasks: it is not called by two of them at the same time
                    _errorHandler = context =>
                    {
                        lock (_errorHandlerLock)
                        {
                            return handler(context);
                        }
                    };
                }
            }

            // A batch converted by the consumer is reused even when its task has not run yet: that task does nothing (see Batch.Start), so the
            // number of batches does not grow when the thread pool is busy
            if (!_free.TryPop(out batch))
            {
#pragma warning disable CA2000 // The parser of the batch is disposed with the batches, in Dispose
                batch = new Batch(parser.CreateBatch(), _binder.Clone(_errorHandler), _maxBatchRows);
#pragma warning restore CA2000
                _batches.Add(batch);
            }

            _filling = batch;
        }

        var batchParser = batch.Parser;
        batchParser.AppendRow(parser);
        if (batchParser.BatchRowCount >= _maxBatchRows || batchParser.BatchLength >= MaxBatchLength)
        {
            StartFilling();
        }
    }

    private void StartFilling()
    {
        if (_filling is { } batch)
        {
            _filling = null;
            batch.Start();
            _pending.Enqueue(batch);
        }
    }

    // The records read before the error are converted and returned before the error is thrown
    private void EndOfRecords(Exception? error)
    {
        _endOfRecords = true;
        StartFilling();
        if (error is not null)
        {
            _readError = ExceptionDispatchInfo.Capture(error);
        }
    }

    private void ReleaseCurrent()
    {
        if (_current is { } batch)
        {
            _current = null;
            batch.Reset();
            _free.Push(batch);
        }
    }

    // The tasks use the buffers of their batch: they must end before the buffers go back to the pool. The batches no task has started are not converted.
    public void Dispose()
    {
        foreach (var batch in CancelBatches())
        {
            batch.WaitForCompletion();
        }

        DisposeBatches();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var batch in CancelBatches())
        {
            if (batch.Task is { } task)
            {
                await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        DisposeBatches();
    }

    // Every batch no task has started is canceled before waiting for the others: a batch does not start while waiting for a slow one
    private List<Batch> CancelBatches()
    {
        var running = new List<Batch>();
        foreach (var batch in _batches)
        {
            if (!batch.TryCancel())
            {
                running.Add(batch);
            }
        }

        return running;
    }

    private void DisposeBatches()
    {
        foreach (var batch in _batches)
        {
            batch.Parser.Dispose();
        }

        _batches.Clear();
        _pending.Clear();
        _free.Clear();
        _filling = null;
        _current = null;
    }

    private sealed class Batch(CsvParser<TChar> parser, CsvRecordBinder<T> binder, int maxRows)
    {
        private readonly T[] _results = new T[maxRows];
        private int _count;

        // Start numbers the conversions of the batch. The task or the consumer claims a conversion by setting _claimed to its number: the task of an
        // earlier conversion, which the consumer did itself, cannot claim a later one, so the batch is reused without waiting for that task
        private int _generation;
        private int _claimed;

        public CsvParser<TChar> Parser => parser;

        public Task? Task { get; private set; }

        public ArraySegment<T> Results => new(_results, 0, _count);

        // The error that stopped the conversion of the batch, after the objects of Results
        public ExceptionDispatchInfo? Error { get; private set; }

        public void Start()
        {
            var generation = ++_generation;
            Task = Task.Factory.StartNew(static state =>
            {
                var (batch, generation) = ((Batch, int))state!;
                if (batch.TryClaim(generation))
                {
                    batch.Convert();
                }
            }, (this, generation), CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        /// <summary>Converts the records of the batch, unless the task or the consumer already does.</summary>
        /// <remarks>
        /// The consumer converts the oldest batch itself when no thread of the pool has started it: it does not wait for the pool, which can be busy, and
        /// the calling thread converts records too.
        /// </remarks>
        /// <returns><see langword="true"/> when the batch was converted by this call.</returns>
        public bool TryConvert()
        {
            if (!TryClaim(_generation))
                return false;

            Convert();
            return true;
        }

        /// <summary>Prevents the task from converting the records when it has not started.</summary>
        /// <returns><see langword="true"/> when no task uses the batch; otherwise, the last task or the consumer converted it.</returns>
        public bool TryCancel() => _generation == 0 || TryClaim(_generation);

        private bool TryClaim(int generation) => Interlocked.CompareExchange(ref _claimed, generation, generation - 1) == generation - 1;

        private void Convert()
        {
            var count = parser.BatchRowCount;
            var results = _results;
            var resultCount = 0;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    parser.MoveToRow(i);
                    if (binder.TryRead(parser, out var item))
                    {
                        results[resultCount++] = item;
                    }
                }
            }
            catch (Exception ex)
            {
                Error = ExceptionDispatchInfo.Capture(ex);
            }

            _count = resultCount;
        }

        public void WaitForCompletion()
        {
            Task?.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
        }

        public void Reset()
        {
            // The objects are not kept alive by the batch once returned
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(_results, 0, _count);
            }

            _count = 0;
            Error = null;
            parser.ClearBatch();
        }
    }
}
