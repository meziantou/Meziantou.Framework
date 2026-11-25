using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebTransport;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A session of <c>Meziantou.Framework.WebTransport</c>, returned by <see cref="HttpDocumentLoader.ConnectWebTransportAsync"/>.</summary>
internal sealed class HttpWebTransportConnection(WebTransportSession session) : WebTransportConnection
{
    // The payload of the datagrams, which the statistics of the QUIC connection do not separate from its overhead
    private long _datagramBytesSent;
    private long _datagramBytesReceived;

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
    {
        await session.SendDatagramAsync(datagram, cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _datagramBytesSent, datagram.Length);
    }

    public override async ValueTask<ReadOnlyMemory<byte>> ReceiveDatagramAsync(CancellationToken cancellationToken)
    {
        var datagram = await session.ReceiveDatagramAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _datagramBytesReceived, datagram.Length);
        return datagram;
    }

    public override ValueTask CloseAsync(uint closeCode, string reason) => session.CloseAsync(closeCode, reason);

    public override ValueTask<WebTransportConnectionStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        // The payload is the data of the streams and of the datagrams; the overhead is the rest of the UDP payload. MsQuic
        // does not count the acknowledged bytes (its stream byte counter grows when a frame is built, before an ACK): the
        // member is absent.
        var statistics = session.GetConnectionStatistics();
        var payloadSent = statistics.StreamBytesSent + Interlocked.Read(ref _datagramBytesSent);
        return ValueTask.FromResult(new WebTransportConnectionStatistics
        {
            BytesSent = payloadSent,
            BytesSentOverhead = Math.Max(0, statistics.BytesSent - payloadSent),
            PacketsSent = statistics.PacketsSent,
            PacketsLost = statistics.PacketsLost,
            BytesReceived = statistics.StreamBytesReceived + Interlocked.Read(ref _datagramBytesReceived),
            PacketsReceived = statistics.PacketsReceived,
            SmoothedRtt = statistics.SmoothedRoundTripTime,
            RttVariation = statistics.RoundTripTimeVariation,
            MinRtt = statistics.MinRoundTripTime,
        });
    }

    public override ValueTask DisposeAsync() => session.DisposeAsync();
}
