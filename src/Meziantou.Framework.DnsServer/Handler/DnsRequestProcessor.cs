using System.Buffers.Binary;
using System.Net;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.Extensions.Logging;
using DnsResponseCode = Meziantou.Framework.DnsServer.Protocol.DnsResponseCode;

namespace Meziantou.Framework.DnsServer.Handler;

/// <summary>
/// Decodes an incoming message, validates it as a server-bound query, invokes the user handler and
/// encodes the reply. Shared by every transport so that the protocol rules live in one place.
/// </summary>
internal sealed class DnsRequestProcessor
{
    /// <summary>The smallest payload size a DNS implementation must accept over UDP (RFC 1035 4.2.1).</summary>
    public const int MinUdpPayloadSize = 512;

    /// <summary>The highest EDNS version this server understands.</summary>
    private const byte SupportedEdnsVersion = 0;

    private readonly DnsRequestDelegateHolder _handlerHolder;
    private readonly DnsServerOptions _options;
    private readonly ILogger<DnsRequestProcessor> _logger;

    public DnsRequestProcessor(DnsRequestDelegateHolder handlerHolder, DnsServerOptions options, ILogger<DnsRequestProcessor> logger)
    {
        _handlerHolder = handlerHolder;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Processes one message and returns the bytes to send back, or <see langword="null"/> when the
    /// message must be dropped without a reply.
    /// </summary>
    /// <param name="maxResponseSize">The largest reply the transport can carry.</param>
    /// <param name="replyWithFormatError">
    /// Whether an unparseable message should be answered with FORMERR. Transports that have no other way
    /// to report the problem want this; DoH reports it as an HTTP status instead.
    /// </param>
    public async ValueTask<byte[]?> ProcessAsync(ReadOnlyMemory<byte> data, DnsServerProtocol protocol, EndPoint remoteEndPoint, int maxResponseSize, CancellationToken cancellationToken, bool replyWithFormatError = true)
    {
        DnsMessage query;
        try
        {
            query = DnsMessageEncoder.DecodeQuery(data.Span);
        }
        catch (DnsProtocolException exception)
        {
            _logger.LogDebug(exception, "Discarding a malformed DNS message from {RemoteEndPoint}", remoteEndPoint);
            return replyWithFormatError ? CreateFormatErrorResponse(data.Span, protocol, maxResponseSize) : null;
        }

        // RFC 5625 4.4: a server must never act on a message that is itself a response. Answering one
        // lets an attacker point two servers at each other and have them exchange packets indefinitely.
        if (query.IsResponse)
        {
            _logger.LogDebug("Discarding a DNS message from {RemoteEndPoint} that has the QR bit set", remoteEndPoint);
            return null;
        }

        var responseSize = GetMaxResponseSize(query.EdnsOptions, protocol, maxResponseSize);
        var context = new DnsRequestContext(query, protocol, remoteEndPoint);

        DnsMessage response;
        if (query.EdnsOptions is { Version: > SupportedEdnsVersion } edns)
        {
            // RFC 6891 6.1.3: answer an unsupported EDNS version with BADVERS rather than pretending
            // to understand it.
            _logger.LogDebug("Rejecting EDNS version {Version} from {RemoteEndPoint}", edns.Version, remoteEndPoint);
            response = context.CreateResponse();
            response.ResponseCode = DnsResponseCode.BadVersion;
        }
        else
        {
            response = await _handlerHolder.Handler(context, cancellationToken).ConfigureAwait(false);
        }

        return DnsMessageEncoder.EncodeResponse(response, responseSize);
    }

    /// <summary>Builds a FORMERR reply for a message that could not be parsed, so the client fails fast instead of timing out.</summary>
    private byte[]? CreateFormatErrorResponse(ReadOnlySpan<byte> data, DnsServerProtocol protocol, int maxResponseSize)
    {
        // Without a complete header there is no ID to echo, so there is nothing worth sending back.
        if (data.Length < 12)
            return null;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if ((flags & 0x8000) != 0)
            return null;

        var response = new DnsMessage
        {
            Id = BinaryPrimitives.ReadUInt16BigEndian(data),
            IsResponse = true,
            OpCode = (DnsOpCode)((flags >> 11) & 0x0F),
            RecursionDesired = (flags & 0x0100) != 0,
            ResponseCode = DnsResponseCode.FormError,
        };

        return DnsMessageEncoder.EncodeResponse(response, GetMaxResponseSize(edns: null, protocol, maxResponseSize));
    }

    private int GetMaxResponseSize(DnsEdnsOptions? edns, DnsServerProtocol protocol, int transportMaxSize)
    {
        if (protocol is not DnsServerProtocol.Udp)
            return transportMaxSize;

        // Never trust the advertised size outright: a small value would make the reply unsendable and a
        // large one turns the server into an amplifier for spoofed queries.
        var advertised = edns?.UdpPayloadSize ?? MinUdpPayloadSize;

        return Math.Clamp(advertised, MinUdpPayloadSize, _options.MaxUdpResponseSize);
    }
}
