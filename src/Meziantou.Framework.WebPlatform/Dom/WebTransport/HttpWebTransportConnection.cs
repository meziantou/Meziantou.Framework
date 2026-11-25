using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebTransport;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A session of <c>Meziantou.Framework.WebTransport</c>, returned by <see cref="HttpDocumentLoader.ConnectWebTransportAsync"/>.</summary>
internal sealed class HttpWebTransportConnection(WebTransportSession session) : WebTransportConnection
{
    private readonly Lazy<Task<WebTransportConnectionCloseInfo>> _closed = new(async () =>
    {
        var info = await session.Closed.ConfigureAwait(false);
        return new WebTransportConnectionCloseInfo(info.ErrorCode, info.Reason);
    });

    public override string Protocol => session.Protocol ?? "";

    public override IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders => session.ResponseHeaders;

    public override int MaxDatagramSize => session.MaxDatagramSize;

    public override Task<WebTransportConnectionCloseInfo> Closed => _closed.Value;

    public override Task Draining => session.Draining;

    public override async ValueTask<WebTransportConnectionStream> OpenStreamAsync(bool bidirectional, bool waitUntilAvailable, CancellationToken cancellationToken)
    {
        try
        {
            var stream = bidirectional
                ? await session.OpenBidirectionalStreamAsync(waitUntilAvailable, cancellationToken).ConfigureAwait(false)
                : await session.OpenUnidirectionalStreamAsync(waitUntilAvailable, cancellationToken).ConfigureAwait(false);
            return new HttpWebTransportConnectionStream(stream);
        }
        catch (WebTransportException ex) when (ex.InnerException is MsQuicException { Error: MsQuicError.StreamLimitReached })
        {
            throw new InvalidOperationException("The server does not allow more streams now.", ex);
        }
    }

    public override async ValueTask<WebTransportConnectionStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await session.AcceptStreamAsync(cancellationToken).ConfigureAwait(false);
        return new HttpWebTransportConnectionStream(stream);
    }

    public override async ValueTask SendDatagramAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
        => await session.SendDatagramAsync(datagram, cancellationToken).ConfigureAwait(false);

    public override ValueTask<ReadOnlyMemory<byte>> ReceiveDatagramAsync(CancellationToken cancellationToken) => session.ReceiveDatagramAsync(cancellationToken);

    public override ValueTask CloseAsync(uint closeCode, string reason) => session.CloseAsync(closeCode, reason);

    public override ValueTask<WebTransportConnectionStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        var statistics = session.GetConnectionStatistics();
        return ValueTask.FromResult(new WebTransportConnectionStatistics
        {
            BytesSent = statistics.StreamBytesSent,
            BytesSentOverhead = Math.Max(0, statistics.BytesSent - statistics.StreamBytesSent),
            BytesAcknowledged = statistics.StreamBytesSent,
            PacketsSent = statistics.PacketsSent,
            PacketsLost = statistics.PacketsLost,
            BytesReceived = statistics.StreamBytesReceived,
            PacketsReceived = statistics.PacketsReceived,
            SmoothedRtt = statistics.SmoothedRoundTripTime,
            RttVariation = statistics.RoundTripTimeVariation,
            MinRtt = statistics.MinRoundTripTime,
        });
    }

    public override ValueTask DisposeAsync() => session.DisposeAsync();
}
