namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An established WebTransport session, returned by <see cref="DocumentLoader.ConnectWebTransportAsync"/>: the network
/// side of the <c>WebTransport</c> objects of the scripts.
/// </summary>
/// <remarks>
/// The library calls the members from any thread, at most one <see cref="AcceptStreamAsync"/> and one
/// <see cref="ReceiveDatagramAsync"/> at a time. It disposes the connection when the session ends.
/// </remarks>
public abstract class WebTransportConnection : IAsyncDisposable
{
    /// <summary>Gets the application protocol the server picked (<c>wt-protocol</c>), or the empty string.</summary>
    public abstract string Protocol { get; }

    /// <summary>Gets the headers of the response to the CONNECT request, without the pseudo-headers.</summary>
    public abstract IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; }

    /// <summary>Gets the maximum size of a datagram, or 0 when datagrams cannot be sent.</summary>
    public abstract int MaxDatagramSize { get; }

    /// <summary>Gets a task that completes when the server closes the session cleanly, with its code and reason. It faults when the session ends abruptly.</summary>
    public abstract Task<WebTransportConnectionCloseInfo> Closed { get; }

    /// <summary>Gets a task that completes when the server asks to end the session (DRAIN_WEBTRANSPORT_SESSION).</summary>
    public abstract Task Draining { get; }

    /// <summary>Gets the number of streams the server can open at the same time, of each type, as the session negotiated it.</summary>
    public virtual (int Bidirectional, int Unidirectional) IncomingStreamLimits => (100, 100);

    /// <summary>Opens a stream.</summary>
    /// <param name="bidirectional"><see langword="true"/> for a bidirectional stream.</param>
    /// <param name="waitUntilAvailable"><see langword="true"/> to wait until the server allows one more stream; <see langword="false"/> to throw an <see cref="InvalidOperationException"/> when it does not allow one now.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    public abstract ValueTask<WebTransportConnectionStream> OpenStreamAsync(bool bidirectional, bool waitUntilAvailable, CancellationToken cancellationToken);

    /// <summary>Waits for the next stream the server opens. Throws once the session is closed.</summary>
    public abstract ValueTask<WebTransportConnectionStream> AcceptStreamAsync(CancellationToken cancellationToken);

    /// <summary>Sends a datagram of at most <see cref="MaxDatagramSize"/> bytes.</summary>
    public abstract ValueTask SendDatagramAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken);

    /// <summary>Waits for the next datagram of the server. Throws once the session is closed.</summary>
    public abstract ValueTask<ReadOnlyMemory<byte>> ReceiveDatagramAsync(CancellationToken cancellationToken);

    /// <summary>Closes the session with an application error code and a reason (CLOSE_WEBTRANSPORT_SESSION).</summary>
    public abstract ValueTask CloseAsync(uint closeCode, string reason);

    /// <summary>Gets the statistics of the connection.</summary>
    public abstract ValueTask<WebTransportConnectionStatistics> GetStatisticsAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();
}
