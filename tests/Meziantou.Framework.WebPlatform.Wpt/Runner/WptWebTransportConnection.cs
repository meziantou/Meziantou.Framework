using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The browser side of a session of the emulated WebTransport server (see <see cref="WptWebTransportSession"/>).</summary>
internal sealed class WptWebTransportConnection(WptWebTransportSession session, string protocol, IReadOnlyList<KeyValuePair<string, string>> responseHeaders) : WebTransportConnection
{
    public override string Protocol => protocol;

    public override IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders => responseHeaders;

    public override int MaxDatagramSize => WptWebTransportSession.MaxDatagramSize;

    public override Task<WebTransportConnectionCloseInfo> Closed => session.ClosedSource.Task;

    public override Task Draining => session.DrainingSource.Task;

    public override (int Bidirectional, int Unidirectional) IncomingStreamLimits => (session.RemoteMaxStreamsBidi, session.RemoteMaxStreamsUni);

    public override ValueTask<WebTransportConnectionStream> OpenStreamAsync(bool bidirectional, bool waitUntilAvailable, CancellationToken cancellationToken) => session.OpenStreamAsync(bidirectional, waitUntilAvailable, cancellationToken);

    public override ValueTask<WebTransportConnectionStream> AcceptStreamAsync(CancellationToken cancellationToken) => session.AcceptStreamAsync(cancellationToken);

    public override ValueTask SendDatagramAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken) => session.SendDatagramAsync(datagram);

    public override ValueTask<ReadOnlyMemory<byte>> ReceiveDatagramAsync(CancellationToken cancellationToken) => session.ReceiveDatagramAsync(cancellationToken);

    public override ValueTask CloseAsync(uint closeCode, string reason)
    {
        session.CloseFromClient(new WebTransportConnectionCloseInfo(closeCode, reason));
        return ValueTask.CompletedTask;
    }

    public override ValueTask<WebTransportConnectionStatistics> GetStatisticsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(session.GetStatistics());

    public override ValueTask DisposeAsync()
    {
        session.DisposeFromClient();
        return ValueTask.CompletedTask;
    }
}
