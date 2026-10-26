using System.Threading.Channels;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The body of a response that another thread writes as it reads it (the body of the <c>Response</c> object that a service
/// worker gives to <c>respondWith()</c>, read on the thread of the worker): the chunks are the
/// <see cref="DocumentResource.RemainingContentBytes"/> of the response given to the client. When the client stops reading
/// the body before its end (e.g. the fetch is aborted), <paramref name="cancel"/> is called once, so that the writer stops
/// reading the body.
/// </summary>
internal sealed class FetchBodyChannel(Action cancel) : IAsyncEnumerable<ReadOnlyMemory<byte>>
{
    private readonly Channel<ReadOnlyMemory<byte>> _channel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private int _canceled;

    /// <summary>Adds a chunk of the body.</summary>
    public void Write(ReadOnlyMemory<byte> chunk) => _channel.Writer.TryWrite(chunk);

    /// <summary>Ends the body.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>Fails the body (e.g. the stream of the body is errored).</summary>
    public void Fail(Exception exception) => _channel.Writer.TryComplete(exception);

    public async IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var completed = false;
        try
        {
            await foreach (var chunk in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return chunk;
            }

            completed = true;
        }
        finally
        {
            if (!completed)
            {
                Cancel();
            }
        }
    }

    /// <summary>Stops the body: the writer stops reading it (the client does not read the rest of the body).</summary>
    public void Cancel()
    {
        if (Interlocked.Exchange(ref _canceled, 1) == 0)
        {
            _channel.Writer.TryComplete();
            cancel();
        }
    }
}
