namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Reads the body of a response that arrives over time (https://fetch.spec.whatwg.org/#concept-body-stream): the bytes
/// received with the headers, then the chunks of <see cref="DocumentResource.RemainingContentBytes"/>, one chunk at a time
/// (the <c>ReadableStream</c> of the body of <c>fetch()</c> pulls them as it is read). The reading stops when the fetch is
/// aborted (the cancellation token) or when the reader is canceled; the chunks are not read before they are needed.
/// </summary>
internal sealed class FetchBodyReader
{
    private readonly Lock _lock = new();
    private readonly ReadOnlyMemory<byte> _first;
    private readonly IAsyncEnumerable<ReadOnlyMemory<byte>> _remaining;
    private readonly CancellationToken _cancellationToken;
    private IAsyncEnumerator<ReadOnlyMemory<byte>>? _enumerator;
    private bool _firstRead;
    private bool _reading;
    private bool _closed;
    private long _length;

    private FetchBodyReader(ReadOnlyMemory<byte> first, IAsyncEnumerable<ReadOnlyMemory<byte>> remaining, CancellationToken cancellationToken)
    {
        _first = first;
        _remaining = remaining;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Returns a reader of the body of a response whose body arrives over time, or null when the body is complete.</summary>
    public static FetchBodyReader? Create(FetchResponse response, CancellationToken cancellationToken)
        => response.Resource is { ContentBytes: { } first, RemainingContentBytes: { } remaining } ? new FetchBodyReader(first, remaining, cancellationToken) : null;

    /// <summary>The number of bytes of the body read so far.</summary>
    public long Length => Interlocked.Read(ref _length);

    /// <summary>
    /// Returns the next chunk of the body (never empty), or null at the end of the body. The task fails with an
    /// <see cref="OperationCanceledException"/> when the fetch is aborted or the reader is canceled, and with another
    /// exception when the body fails (e.g. the connection is closed before the end of the body).
    /// </summary>
    public async Task<ReadOnlyMemory<byte>?> ReadAsync()
    {
        IAsyncEnumerator<ReadOnlyMemory<byte>> enumerator;
        lock (_lock)
        {
            if (_closed || _cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(_cancellationToken);

            if (_reading)
                throw new InvalidOperationException("A chunk is already being read");

            if (!_firstRead)
            {
                _firstRead = true;
                if (!_first.IsEmpty)
                {
                    _length += _first.Length;
                    return _first;
                }
            }

            _reading = true;
            enumerator = _enumerator ??= _remaining.GetAsyncEnumerator(_cancellationToken);
        }

        var end = true;
        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!enumerator.Current.IsEmpty)
                {
                    end = false;
                    Interlocked.Add(ref _length, enumerator.Current.Length);
                    return enumerator.Current;
                }
            }

            return null;
        }
        finally
        {
            bool dispose;
            lock (_lock)
            {
                _reading = false;
                dispose = end || _closed || _cancellationToken.IsCancellationRequested;
                _closed |= dispose;
            }

            if (dispose)
            {
                await DisposeEnumeratorAsync(enumerator).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Stops reading the body (the stream is canceled, or the fetch is aborted): the rest of the body is not read.</summary>
    public void Cancel()
    {
        IAsyncEnumerator<ReadOnlyMemory<byte>>? enumerator;
        lock (_lock)
        {
            if (_closed)
                return;

            _closed = true;

            // A chunk being read is disposed when the read completes
            enumerator = _reading ? null : _enumerator;
        }

        if (enumerator is not null)
        {
            _ = DisposeEnumeratorAsync(enumerator);
        }

        // The writer of a body that is not read yet stops reading it (e.g. the stream of a service worker is canceled)
        if (_remaining is FetchBodyChannel channel)
        {
            channel.Cancel();
        }
    }

    private static async Task DisposeEnumeratorAsync(IAsyncEnumerator<ReadOnlyMemory<byte>> enumerator)
    {
        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The rest of the body is not needed
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
