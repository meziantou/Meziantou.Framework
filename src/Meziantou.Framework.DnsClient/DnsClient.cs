using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Quic;
using Meziantou.Framework.DnsClient.Helpers;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Transport;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient;

/// <summary>A DNS client supporting UDP, TCP, DNS over TLS, DNS over HTTPS, DNS over QUIC, DNSSEC, EDNS, IDN, and reverse lookups.</summary>
[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace")]
public sealed class DnsClient : IDisposable
{
    private readonly IDnsTransport _transport;
    private readonly DnsClientOptions _options;
    private readonly DnsClientProtocol _protocol;

    /// <summary>Used to re-issue a truncated UDP query over TCP. Null for every other protocol.</summary>
    private readonly DnsTcpTransport? _tcpFallback;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClient"/> class.
    /// </summary>
    /// <param name="server">The DNS server address (IP address, hostname, or URL for DNS over HTTPS).</param>
    /// <param name="protocol">The DNS transport protocol to use.</param>
    public DnsClient(string server, DnsClientProtocol protocol)
        : this(server, protocol, options: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClient"/> class.
    /// </summary>
    /// <param name="server">The DNS server address (IP address, hostname, or URL for DNS over HTTPS).</param>
    /// <param name="protocol">The DNS transport protocol to use.</param>
    /// <param name="options">Optional configuration options.</param>
    public DnsClient(string server, DnsClientProtocol protocol, DnsClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(server);

        _options = options ?? new DnsClientOptions();
        ValidateOptions(_options);
        _protocol = protocol;
        _transport = CreateTransport(server, protocol, _options);
        _tcpFallback = protocol is DnsClientProtocol.Udp && _options.RetryTruncatedOverTcp
            ? CreateTcpTransport(server, _options)
            : null;
    }

    internal DnsClient(IDnsTransport transport, DnsClientProtocol protocol, DnsClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(transport);

        _options = options ?? new DnsClientOptions();
        ValidateOptions(_options);
        _protocol = protocol;
        _transport = transport;
    }

    /// <summary>Sends a DNS query for the specified domain name and record type.</summary>
    /// <param name="name">The domain name to query. Unicode names are automatically converted to punycode.</param>
    /// <param name="type">The DNS record type to query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The DNS response message.</returns>
    public Task<DnsResponseMessage> QueryAsync(string name, DnsQueryType type, CancellationToken cancellationToken = default)
    {
        return QueryAsync(name, type, DnsQueryClass.IN, cancellationToken);
    }

    /// <summary>Sends a DNS query for the specified domain name, record type, and class.</summary>
    /// <param name="name">The domain name to query. Unicode names are automatically converted to punycode.</param>
    /// <param name="type">The DNS record type to query.</param>
    /// <param name="queryClass">The DNS query class.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The DNS response message.</returns>
    /// <exception cref="DnsProtocolException">The response is malformed, or does not answer the query that was sent.</exception>
    /// <exception cref="TimeoutException"><see cref="DnsClientOptions.Timeout"/> elapsed before a response arrived.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public Task<DnsResponseMessage> QueryAsync(string name, DnsQueryType type, DnsQueryClass queryClass, CancellationToken cancellationToken = default)
    {
        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion(name, type, queryClass));

        return SendAsync(query, cancellationToken);
    }

    /// <summary>Performs a reverse DNS lookup for the specified IP address.</summary>
    /// <param name="address">The IP address to look up (IPv4 or IPv6).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The DNS response message containing PTR records.</returns>
    public Task<DnsResponseMessage> ReverseLookupAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        var reverseDomain = ReverseLookupHelper.GetReverseLookupDomain(address);
        return QueryAsync(reverseDomain, DnsQueryType.PTR, DnsQueryClass.IN, cancellationToken);
    }

    /// <summary>Sends a DNS query message and returns the response.</summary>
    /// <param name="message">The DNS query message to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The DNS response message.</returns>
    /// <remarks>The message is not modified; the client's options are applied to an internal copy.</remarks>
    /// <exception cref="DnsProtocolException">The response is malformed, or does not answer the query that was sent.</exception>
    /// <exception cref="TimeoutException"><see cref="DnsClientOptions.Timeout"/> elapsed before a response arrived.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public async Task<DnsResponseMessage> SendAsync(DnsQueryMessage message, CancellationToken cancellationToken = default)
    {
        return await SendCoreAsync(message, validateResponse: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DnsResponseMessage> SendCoreAsync(DnsQueryMessage message, bool validateResponse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Apply the options to a copy: the caller owns the message and may reuse it across clients or calls.
        var effectiveMessage = CloneWithOptions(message);

        var questionName = effectiveMessage.Questions.Count > 0 ? effectiveMessage.Questions[0].Name : "unknown";
        var questionType = effectiveMessage.Questions.Count > 0 ? effectiveMessage.Questions[0].Type.ToString() : "unknown";
        var questionClass = effectiveMessage.Questions.Count > 0 ? effectiveMessage.Questions[0].QueryClass.ToString() : "unknown";

        using var activity = DnsTelemetry.ActivitySource.StartActivity("dns.query", ActivityKind.Client);
        activity?.SetTag("dns.question.name", questionName);
        activity?.SetTag("dns.question.type", questionType);
        activity?.SetTag("dns.question.class", questionClass);
        activity?.SetTag("network.transport", GetTransportName(_protocol));

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.Timeout);

            DnsResponseMessage response;
            try
            {
                response = await ExchangeAsync(effectiveMessage, cts.Token).ConfigureAwait(false);

                // RFC 1035 4.2.1 / RFC 7766 6.2.2: a truncated UDP answer must be retried over TCP, otherwise the
                // caller silently receives a partial RRset.
                if (response.Header.IsTruncated && _protocol is DnsClientProtocol.Udp && _options.RetryTruncatedOverTcp && _tcpFallback is not null)
                {
                    response = await ExchangeAsync(effectiveMessage, cts.Token, _tcpFallback).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The linked token fired because of our own timeout, not because the caller cancelled. Surfacing a
                // bare OperationCanceledException here would be indistinguishable from caller cancellation.
                throw new TimeoutException($"The DNS query timed out after {_options.Timeout}.", ex);
            }

            if (validateResponse && _options.DnssecValidationMode is DnssecValidationMode.Local)
            {
                var validator = new DnssecValidator(
                    SendWithoutValidationAsync,
                    _options.DnssecTrustAnchors,
                    _options.TimeProvider,
                    _options.EdnsUdpPayloadSize);

                try
                {
                    response.DnssecValidationResult = await validator.ValidateAsync(response, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"DNSSEC validation timed out after {_options.Timeout}.", ex);
                }

                activity?.SetTag("dns.dnssec.validation_status", response.DnssecValidationResult.Status.ToString());
            }

            activity?.SetTag("dns.response.code", response.Header.ResponseCode.ToString());

            if (response.Header.ResponseCode != DnsResponseCode.NoError)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"DNS response code: {response.Header.ResponseCode}");
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private async Task<DnsResponseMessage> ExchangeAsync(DnsQueryMessage message, CancellationToken cancellationToken, IDnsTransport? transport = null)
    {
        var queryBytes = DnsMessageEncoder.EncodeQuery(message, out var queryId);
        var responseBytes = await (transport ?? _transport).SendAsync(queryBytes, cancellationToken).ConfigureAwait(false);
        var response = DnsMessageEncoder.DecodeResponse(
            responseBytes,
            preserveRawRecordData: _options.DnssecValidationMode is DnssecValidationMode.Local);

        ValidateResponseMatchesQuery(message, queryId, response);
        return response;
    }

    private Task<DnsResponseMessage> SendWithoutValidationAsync(DnsQueryMessage message, CancellationToken cancellationToken)
    {
        return SendCoreAsync(message, validateResponse: false, cancellationToken);
    }

    /// <summary>Releases the resources used by this instance.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _transport.Dispose();
        _tcpFallback?.Dispose();
    }

    /// <summary>
    /// Rejects a response that does not answer the query that was sent. Without this check any party able to deliver a
    /// datagram — an off-path spoofer on UDP, or an on-path attacker — can substitute arbitrary records.
    /// </summary>
    private static void ValidateResponseMatchesQuery(DnsQueryMessage query, ushort queryId, DnsResponseMessage response)
    {
        if (response.Header.Id != queryId)
            throw new DnsProtocolException($"The DNS response identifier (0x{response.Header.Id:X4}) does not match the query identifier (0x{queryId:X4}).");

        if (!response.Header.IsResponse)
            throw new DnsProtocolException("The DNS message is not a response (the QR bit is not set).");

        if (response.Header.OpCode != query.OpCode)
            throw new DnsProtocolException($"The DNS response operation code ({response.Header.OpCode}) does not match the query operation code ({query.OpCode}).");

        // A FormErr response may legitimately omit the question section because the server could not parse it.
        if (response.Questions.Count is 0 && response.Header.ResponseCode is DnsResponseCode.FormError)
            return;

        if (response.Questions.Count != query.Questions.Count)
            throw new DnsProtocolException($"The DNS response contains {response.Questions.Count} question(s) but the query contained {query.Questions.Count}.");

        for (var i = 0; i < query.Questions.Count; i++)
        {
            var asked = query.Questions[i];
            var echoed = response.Questions[i];

            if (echoed.Type != asked.Type || echoed.QueryClass != asked.QueryClass ||
                !DnsNameComparer.Equals(echoed.Name, asked.Name))
            {
                throw new DnsProtocolException($"The DNS response question ({echoed.Name} {echoed.Type} {echoed.QueryClass}) does not match the query question ({asked.Name} {asked.Type} {asked.QueryClass}).");
            }
        }
    }

    private static string GetTransportName(DnsClientProtocol protocol)
    {
        return protocol switch
        {
            DnsClientProtocol.Udp => "udp",
            DnsClientProtocol.Tcp => "tcp",
            DnsClientProtocol.Tls => "tls",
            DnsClientProtocol.Https => "https",
            DnsClientProtocol.Quic => "quic",
            _ => "unknown",
        };
    }

    private static IDnsTransport CreateTransport(string server, DnsClientProtocol protocol, DnsClientOptions options)
    {
        return protocol switch
        {
            DnsClientProtocol.Udp => CreateUdpTransport(server, options),
            DnsClientProtocol.Tcp => CreateTcpTransport(server, options),
            DnsClientProtocol.Tls => CreateTlsTransport(server, options),
            DnsClientProtocol.Https => CreateHttpsTransport(server, options),
            DnsClientProtocol.Quic => CreateQuicTransport(server, options),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported DNS protocol."),
        };
    }

    private static void ValidateOptions(DnsClientOptions options)
    {
        if (options.DnssecValidationMode is DnssecValidationMode.Local && !options.EnableEdns)
            throw new ArgumentException("Local DNSSEC validation requires EDNS to be enabled.", nameof(options));

        if (options.Timeout <= TimeSpan.Zero && options.Timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, "The timeout must be positive, or Timeout.InfiniteTimeSpan for no timeout.");
    }

    /// <summary>
    /// Produces the message that will actually go on the wire, with the client's options applied. The caller's
    /// message is never modified: it may be reused across calls or across clients with different options.
    /// </summary>
    private DnsQueryMessage CloneWithOptions(DnsQueryMessage message)
    {
        var localValidation = _options.DnssecValidationMode is DnssecValidationMode.Local;
        var requiresDnssecEdns = localValidation || _options.DnssecOk;

        var clone = new DnsQueryMessage
        {
            Id = message.Id,
            OpCode = message.OpCode,
            RecursionDesired = message.RecursionDesired,
            CheckingDisabled = message.CheckingDisabled || localValidation,
        };

        foreach (var question in message.Questions)
        {
            // Unicode names are converted here rather than in QueryAsync so that SendAsync gets the same treatment.
            clone.Questions.Add(new DnsQuestion(IdnHelper.ToAscii(question.Name), question.Type, question.QueryClass));
        }

        var edns = message.EdnsOptions;
        if (edns is null && _options.EnableEdns)
        {
            edns = new DnsEdnsOptions();
        }

        if (edns is not null)
        {
            clone.EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = edns.UdpPayloadSize is 0 ? _options.EdnsUdpPayloadSize : edns.UdpPayloadSize,
                Version = edns.Version,
                ExtendedRCode = edns.ExtendedRCode,
                DnssecOk = edns.DnssecOk || requiresDnssecEdns,
            };
        }

        return clone;
    }

    private static DnsUdpTransport CreateUdpTransport(string server, DnsClientOptions options)
    {
        var endpoint = ParseEndpoint(server, defaultPort: 53, options);
        return new DnsUdpTransport(endpoint);
    }

    private static DnsTcpTransport CreateTcpTransport(string server, DnsClientOptions options)
    {
        var endpoint = ParseEndpoint(server, defaultPort: 53, options);
        return new DnsTcpTransport(endpoint);
    }

    private static DnsTlsTransport CreateTlsTransport(string server, DnsClientOptions options)
    {
        var (host, endpoint) = ParseHostAndEndpoint(server, defaultPort: 853, options);
        return new DnsTlsTransport(host, endpoint);
    }

    private static DnsHttpsTransport CreateHttpsTransport(string server, DnsClientOptions options)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid DNS over HTTPS URL: {server}", nameof(server));

        // DNS over HTTPS exists to keep queries off the plain network; accepting http:// would silently defeat that.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            throw new ArgumentException($"DNS over HTTPS requires an https:// URL, but the scheme was '{uri.Scheme}'.", nameof(server));

        return new DnsHttpsTransport(uri, options.HttpHandler, options.HttpVersion, options.HttpVersionPolicy);
    }

    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance")]
    private static IDnsTransport CreateQuicTransport(string server, DnsClientOptions options)
    {
        // Fail at construction rather than on every query, so callers can catch this and fall back to another protocol.
        if (!QuicConnection.IsSupported)
            throw new PlatformNotSupportedException("DNS over QUIC is not supported on this platform.");

        var (host, endpoint) = ParseHostAndEndpoint(server, defaultPort: 853, options);
        return new DnsQuicTransport(host, endpoint);
    }

    private static IPEndPoint ParseEndpoint(string server, int defaultPort, DnsClientOptions? options)
    {
        return ParseHostAndEndpoint(server, defaultPort, options).Endpoint;
    }

    private static (string Host, IPEndPoint Endpoint) ParseHostAndEndpoint(string server, int defaultPort, DnsClientOptions? options)
    {
        // The bracketed form must be tested first: IPAddress.TryParse accepts "[::1]:5353" and silently discards the
        // port, which would send the query to the default port instead of the one the caller asked for.
        if (TryParseBracketedHostAndPort(server, out var bracketedHost, out var bracketedPort))
            return (bracketedHost, CreateEndpoint(bracketedHost, bracketedPort, server, options));

        if (IPAddress.TryParse(server, out var address))
            return (server, new IPEndPoint(address, defaultPort));

        // Any remaining colon means host:port - a DNS hostname cannot contain one, and bare IPv6 literals were
        // already handled above. A malformed port must be reported against the server argument rather than being
        // silently treated as part of a hostname.
        var colonIndex = server.LastIndexOf(':', StringComparison.Ordinal);
        if (colonIndex > 0)
        {
            if (!TryParsePort(server.AsSpan(colonIndex + 1), out var port))
                throw new ArgumentException($"Invalid port in DNS server address: {server}", nameof(server));

            var host = server[..colonIndex];
            return (host, CreateEndpoint(host, port, server, options));
        }

        return (server, CreateEndpoint(server, defaultPort, server, options));
    }

    private static IPEndPoint CreateEndpoint(string host, int port, string server, DnsClientOptions? options)
    {
        if (IPAddress.TryParse(host, out var address))
            return new IPEndPoint(address, port);

        var resolved = ResolveHost(host, options);
        if (resolved.Length is 0)
            throw new ArgumentException($"Could not resolve host '{host}' from DNS server address '{server}'.", nameof(server));

        return new IPEndPoint(resolved[0], port);
    }

    private static bool TryParsePort(ReadOnlySpan<char> value, out int port)
    {
        // NumberStyles.None rejects the leading whitespace and sign that NumberStyles.Integer would accept.
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
    }

    private static bool TryParseBracketedHostAndPort(string server, [NotNullWhen(true)] out string? host, out int port)
    {
        host = null;
        port = 0;
        if (!server.StartsWith('[', StringComparison.Ordinal))
            return false;

        var closingBracketIndex = server.IndexOf(']', StringComparison.Ordinal);
        if (closingBracketIndex <= 1 || closingBracketIndex + 2 > server.Length || server[closingBracketIndex + 1] != ':')
            return false;

        if (!TryParsePort(server.AsSpan(closingBracketIndex + 2), out port))
            return false;

        host = server[1..closingBracketIndex];
        return true;
    }

    private static IPAddress[] ResolveHost(string host, DnsClientOptions? options)
    {
        var resolved = options?.ServerAddressResolver?.Invoke(host);
        if (resolved is not null)
            return resolved.ToArray();

        return Dns.GetHostAddresses(host);
    }
}
