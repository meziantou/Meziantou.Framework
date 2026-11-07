using System.Runtime.ExceptionServices;

namespace Meziantou.Framework;

/// <summary>Reads the source of a cached async enumerable on behalf of all its consumers. The caller serializes the calls.</summary>
internal sealed class CachedAsyncEnumerableSource<T> : IAsyncDisposable
{
    private readonly IAsyncEnumerable<T> _enumerable;
    private readonly CancellationTokenSource _disposeCts = new();
    private IAsyncEnumerator<T>? _enumerator;
    private Task<bool>? _pendingMoveNext;
    private ExceptionDispatchInfo? _exception;
    private bool _completed;

    public CachedAsyncEnumerableSource(IAsyncEnumerable<T> enumerable)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        _enumerable = enumerable;
    }

    public async ValueTask<Optional<T>> MoveNextAsync(CancellationToken cancellationToken)
    {
        // The source failed: replay its exception instead of ending the sequence early
        _exception?.Throw();

        if (_completed)
            return default;

        // The source enumerator is shared by all the consumers, so it must not depend on the token of the
        // consumer that happens to create it. It is only cancelled when the cached enumerable is disposed.
        _enumerator ??= _enumerable.GetAsyncEnumerator(_disposeCts.Token);

        bool hasNext;
        var pending = _pendingMoveNext;
        try
        {
            if (pending is null && !cancellationToken.CanBeCanceled)
            {
                hasNext = await _enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            else
            {
                // A consumer that stops waiting leaves the operation running: the next consumer resumes it,
                // as the source does not support starting a new MoveNextAsync before the previous one completes.
                pending ??= _enumerator.MoveNextAsync().AsTask();
                _pendingMoveNext = pending;
                hasNext = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                _pendingMoveNext = null;
            }
        }
        catch (Exception ex) when (pending is null || pending.IsFaulted || pending.IsCanceled)
        {
            // The source failed. Otherwise, the consumer was cancelled and the source operation is still pending.
            _pendingMoveNext = null;
            _exception = ExceptionDispatchInfo.Capture(ex);
            await DisposeEnumeratorAsync().ConfigureAwait(false);
            throw;
        }

        if (hasNext)
            return new(_enumerator.Current);

        // There are no more items, we can dispose the underlying enumerator
        _completed = true;
        await DisposeEnumeratorAsync().ConfigureAwait(false);
        return default;
    }

    /// <summary>Cancels the source. Thread-safe.</summary>
    public void Cancel() => _disposeCts.Cancel();

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        await DisposeEnumeratorAsync().ConfigureAwait(false);
        _disposeCts.Dispose();
    }

    private async ValueTask DisposeEnumeratorAsync()
    {
        // An async iterator cannot be disposed while a MoveNextAsync operation is running
        var pending = _pendingMoveNext;
        _pendingMoveNext = null;
        if (pending is not null)
        {
            await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        var enumerator = _enumerator;
        _enumerator = null;
        if (enumerator is not null)
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}
