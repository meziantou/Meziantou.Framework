using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The strips or tiles of a page decoded by several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>):
/// each block is compressed on its own and its predictor works within its rows, so the blocks do not depend on each
/// other. The blocks are decoded by batches of one block per worker and handed over in their order, on the calling
/// thread, so the pixels and the errors are those of one worker.
/// </summary>
/// <remarks>
/// <para>
/// The calling thread alone reads the input and charges it (<see cref="TiffBlockReader.Fetch"/>): a batch ends at the
/// first block that cannot be read. The workers only decompress and transform the rows of their block, in a workspace of
/// their own rented on the calling thread before the first batch, so the live allocations do not depend on the timing of
/// the workers. The memory of a batch is one block per worker: its rows, its working memory and its compressed bytes when
/// they are copied.
/// </para>
/// <para>
/// The error is the one of the first block that fails, after the blocks before it are handed over, a cancellation
/// first. There are several workers only when the scope can hold the working memory of all of them: a limit that would
/// fail them is left to one worker, so it fails as one worker does.
/// </para>
/// </remarks>
internal sealed class TiffBlockWorkers : IDisposable
{
    private readonly TiffBlockReader _reader;
    private readonly TiffPage _page;
    private readonly CancellationToken _cancellationToken;
    private readonly TiffBlockReader.Workspace[] _workspaces;
    private readonly TiffBlockReader.FetchedBlock[] _blocks;
    private readonly ExceptionDispatchInfo?[] _errors;
    private int _start;
    private int _count;

    private TiffBlockWorkers(RandomAccessSource source, TiffPage page, TiffBlockReader reader, int workers)
    {
        _reader = reader;
        _page = page;
        _cancellationToken = source.Context.CancellationToken;
        _workspaces = new TiffBlockReader.Workspace[workers];
        _blocks = new TiffBlockReader.FetchedBlock[workers];
        _errors = new ExceptionDispatchInfo?[workers];
        try
        {
            for (var i = 0; i < workers; i++)
            {
                // The first worker decodes with the memory of the reader, which a decoding by one worker would rent
                var workspace = i == 0 ? reader.OwnWorkspace : new TiffBlockReader.Workspace(source.Scope);
                _workspaces[i] = workspace;
                workspace.Prepare(page, source.Limits);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Creates the workers of a page, or returns <see langword="null"/> when its blocks are decoded by one worker.</summary>
    /// <param name="source">The input, whose context gives the number of workers and whose scope is charged for them.</param>
    /// <param name="page">The page.</param>
    /// <param name="reader">The reader of the blocks of the page.</param>
    /// <returns>The workers, which the caller disposes, or <see langword="null"/>.</returns>
    public static TiffBlockWorkers? Create(RandomAccessSource source, TiffPage page, TiffBlockReader reader)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(reader);
        var workers = GetWorkerCount(page, source.Scope, source.Context.Configuration.MaxDegreeOfParallelism);
        return workers > 1 ? new TiffBlockWorkers(source, page, reader, workers) : null;
    }

    /// <summary>
    /// Gets the number of workers of a page: one below <see cref="RowBands.MinPixelsForParallelism"/> pixels, for a single
    /// block, for blocks that cost little to decode (<see cref="TiffBlockReader.CanDecodeInParallel"/>), and as many as the
    /// live bytes the scope has left can give working memory to.
    /// </summary>
    internal static int GetWorkerCount(TiffPage page, AllocationScope scope, int maxDegreeOfParallelism)
    {
        var blockCount = page.BlockOffsets.Length;
        if (maxDegreeOfParallelism <= 1 || blockCount < 2 || (long)page.Size.Width * page.Size.Height < RowBands.MinPixelsForParallelism || !TiffBlockReader.CanDecodeInParallel(page))
            return 1;

        var workers = Math.Min(maxDegreeOfParallelism, blockCount);
        var available = Math.Max(0, scope.Limit - scope.LiveBytes);
        return (int)Math.Clamp(available / TiffBlockReader.GetWorkerBytes(page, scope), 1, workers);
    }

    /// <summary>Gets the rows of the next block, decoding the batch that starts at it when it is not decoded yet.</summary>
    /// <param name="index">The index of the block: the blocks are read in their order.</param>
    /// <returns>The rows of the block that belong to the page, valid until the next call.</returns>
    /// <exception cref="InvalidImageContentException">The block, or the input, is malformed.</exception>
    public ReadOnlySpan<byte> Next(int index)
    {
        if (index < _start || index > _start + _count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "The blocks are read in their order.");

        if (index == _start + _count)
        {
            DecodeBatch(index);
        }

        var slot = index - _start;
        _errors[slot]?.Throw();
        return _workspaces[slot].GetBlock(_page).Span[..(GetRowCount(index) * _page.BlockRowLength)];
    }

    public void Dispose()
    {
        ReleaseBatch();
        for (var i = 0; i < _workspaces.Length; i++)
        {
            // The reader disposes its own workspace
            if (i > 0)
            {
                _workspaces[i]?.Dispose();
            }

            _workspaces[i] = null!;
        }
    }

    /// <summary>Gets the number of rows of a block that belong to the page (the blocks of the last row hold fewer).</summary>
    private int GetRowCount(int index)
    {
        var page = _page;
        var blockRow = page.IsTiled ? index / page.BlocksAcross : index;
        return Math.Min(page.BlockHeight, page.Size.Height - (blockRow * page.BlockHeight));
    }

    private void DecodeBatch(int start)
    {
        ReleaseBatch();
        _start = start;
        var total = _page.BlockOffsets.Length;
        var count = 0;
        while (count < _workspaces.Length && start + count < total)
        {
            // The input is read on this thread, in the order of the blocks; the batch ends at the first block that cannot be read
            var slot = count++;
            try
            {
                _blocks[slot] = _reader.Fetch(start + slot, GetRowCount(start + slot), _workspaces[slot]);
            }
            catch (Exception exception)
            {
                _errors[slot] = ExceptionDispatchInfo.Capture(exception);
                break;
            }
        }

        _count = count;
        RowBands.RunParallel(count, slot =>
        {
            if (_errors[slot] is not null)
                return;

            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var workspace = _workspaces[slot];
                _reader.Decompress(_blocks[slot], workspace);
                _reader.Finish(_blocks[slot], workspace);
            }
            catch (Exception exception)
            {
                _errors[slot] = ExceptionDispatchInfo.Capture(exception);
            }
        });

        for (var slot = 0; slot < count; slot++)
        {
            if (_errors[slot]?.SourceException is OperationCanceledException)
                _errors[slot]!.Throw();
        }
    }

    private void ReleaseBatch()
    {
        for (var slot = 0; slot < _count; slot++)
        {
            _blocks[slot].Dispose();
            _blocks[slot] = default;
            _errors[slot] = null;
        }

        _count = 0;
    }
}
