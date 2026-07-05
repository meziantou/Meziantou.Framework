using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Meziantou.Framework.DnsClient.Helpers;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Transport;

namespace Meziantou.Framework.DnsClient;

/// <summary>A DNS client supporting UDP, TCP, DNS over TLS, DNS over HTTPS, DNS over QUIC, DNSSEC, EDNS, IDN, and reverse lookups.</summary>
[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace")]
public sealed class DnsClient : IDisposable
{
    private readonly IDnsTransport _transport;
    private readonly DnsClientOptions _options;
    private readonly DnsClientProtocol _protocol;

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
    public Task<DnsResponseMessage> QueryAsync(string name, DnsQueryType type, DnsQueryClass queryClass, CancellationToken cancellationToken = default)
    {
        var asciiName = IdnHelper.ToAscii(name);

        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion(asciiName, type, queryClass));

        if (_options.EnableEdns)
        {
            query.EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = _options.EdnsUdpPayloadSize,
                DnssecOk = _options.DnssecOk,
            };
        }

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
    public async Task<DnsResponseMessage> SendAsync(DnsQueryMessage message, CancellationToken cancellationToken = default)
    {
        return await SendCoreAsync(message, validateResponse: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DnsResponseMessage> SendCoreAsync(DnsQueryMessage message, bool validateResponse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateOptions(_options);
        ApplyOptions(message);

        var questionName = message.Questions.Count > 0 ? message.Questions[0].Name : "unknown";
        var questionType = message.Questions.Count > 0 ? message.Questions[0].Type.ToString() : "unknown";
        var questionClass = message.Questions.Count > 0 ? message.Questions[0].QueryClass.ToString() : "unknown";

        using var activity = DnsTelemetry.ActivitySource.StartActivity("dns.query", ActivityKind.Client);
        activity?.SetTag("dns.question.name", questionName);
        activity?.SetTag("dns.question.type", questionType);
        activity?.SetTag("dns.question.class", questionClass);
        activity?.SetTag("network.transport", GetTransportName(_protocol));

        try
        {
            var queryBytes = DnsMessageEncoder.EncodeQuery(message);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.Timeout);

            var responseBytes = await _transport.SendAsync(queryBytes, cts.Token).ConfigureAwait(false);
            var response = DnsMessageEncoder.DecodeResponse(
                responseBytes,
                preserveRawRecordData: _options.DnssecValidationMode is DnssecValidationMode.Local);

            if (validateResponse && _options.DnssecValidationMode is DnssecValidationMode.Local)
            {
                var validator = new DnssecValidator(
                    SendWithoutValidationAsync,
                    _options.DnssecTrustAnchors,
                    _options.TimeProvider);
                response.DnssecValidationResult = await validator.ValidateAsync(response, cts.Token).ConfigureAwait(false);
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

    private Task<DnsResponseMessage> SendWithoutValidationAsync(DnsQueryMessage message, CancellationToken cancellationToken)
    {
        return SendCoreAsync(message, validateResponse: false, cancellationToken);
    }

    /// <summary>Releases the resources used by this instance.</summary>
    public void Dispose()
    {
        _transport.Dispose();
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

        if (options.DnssecTrustAnchors is null)
            throw new ArgumentException("DNSSEC trust anchors cannot be null.", nameof(options));

        if (options.TimeProvider is null)
            throw new ArgumentException("The DNSSEC time provider cannot be null.", nameof(options));
    }

    private void ApplyOptions(DnsQueryMessage message)
    {
        var localValidation = _options.DnssecValidationMode is DnssecValidationMode.Local;
        var requiresDnssecEdns = localValidation || _options.DnssecOk;

        if (_options.EnableEdns && message.EdnsOptions is null && requiresDnssecEdns)
        {
            message.EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = _options.EdnsUdpPayloadSize,
            };
        }

        if (message.EdnsOptions is not null)
        {
            if (message.EdnsOptions.UdpPayloadSize == 0)
            {
                message.EdnsOptions.UdpPayloadSize = _options.EdnsUdpPayloadSize;
            }

            if (requiresDnssecEdns)
            {
                message.EdnsOptions.DnssecOk = true;
            }
        }

        if (localValidation)
        {
            message.CheckingDisabled = true;
        }
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

        return new DnsHttpsTransport(uri, options.HttpHandler, options.HttpVersion, options.HttpVersionPolicy);
    }

    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance")]
    private static IDnsTransport CreateQuicTransport(string server, DnsClientOptions options)
    {
        var (host, endpoint) = ParseHostAndEndpoint(server, defaultPort: 853, options);
        return new DnsQuicTransport(host, endpoint);
    }

    private static IPEndPoint ParseEndpoint(string server, int defaultPort, DnsClientOptions? options)
    {
        if (IPAddress.TryParse(server, out var address))
            return new IPEndPoint(address, defaultPort);

        if (TryParseBracketedHostAndPort(server, out var bracketedHost, out var bracketedPort))
        {
            if (IPAddress.TryParse(bracketedHost, out var hostAddress))
                return new IPEndPoint(hostAddress, bracketedPort);

            var addresses = ResolveHost(bracketedHost, options);
            if (addresses.Length is 0)
                throw new ArgumentException($"Could not resolve host: {bracketedHost}", nameof(server));

            return new IPEndPoint(addresses[0], bracketedPort);
        }

        // Try host:port format
        var colonIndex = server.LastIndexOf(':', StringComparison.Ordinal);
        if (colonIndex > 0 && int.TryParse(server.AsSpan(colonIndex + 1), System.Globalization.CultureInfo.InvariantCulture, out var port))
        {
            var host = server[..colonIndex];
            if (IPAddress.TryParse(host, out var hostAddress))
                return new IPEndPoint(hostAddress, port);

            var addresses = ResolveHost(host, options);
            if (addresses.Length is 0)
                throw new ArgumentException($"Could not resolve host: {host}", nameof(server));

            return new IPEndPoint(addresses[0], port);
        }

        // Resolve hostname
        var resolved = ResolveHost(server, options);
        if (resolved.Length is 0)
            throw new ArgumentException($"Could not resolve host: {server}", nameof(server));

        return new IPEndPoint(resolved[0], defaultPort);
    }

    private static (string Host, IPEndPoint Endpoint) ParseHostAndEndpoint(string server, int defaultPort, DnsClientOptions? options)
    {
        if (IPAddress.TryParse(server, out var address))
            return (server, new IPEndPoint(address, defaultPort));

        if (TryParseBracketedHostAndPort(server, out var bracketedHost, out var bracketedPort))
        {
            if (IPAddress.TryParse(bracketedHost, out var hostAddress))
                return (bracketedHost, new IPEndPoint(hostAddress, bracketedPort));

            var addresses = ResolveHost(bracketedHost, options);
            if (addresses.Length is 0)
                throw new ArgumentException($"Could not resolve host: {bracketedHost}", nameof(server));

            return (bracketedHost, new IPEndPoint(addresses[0], bracketedPort));
        }

        var colonIndex = server.LastIndexOf(':', StringComparison.Ordinal);
        if (colonIndex > 0 && int.TryParse(server.AsSpan(colonIndex + 1), System.Globalization.CultureInfo.InvariantCulture, out var port))
        {
            var host = server[..colonIndex];
            if (IPAddress.TryParse(host, out var hostAddress))
                return (host, new IPEndPoint(hostAddress, port));

            var addresses = ResolveHost(host, options);
            if (addresses.Length is 0)
                throw new ArgumentException($"Could not resolve host: {host}", nameof(server));

            return (host, new IPEndPoint(addresses[0], port));
        }

        var resolved = ResolveHost(server, options);
        if (resolved.Length is 0)
            throw new ArgumentException($"Could not resolve host: {server}", nameof(server));

        return (server, new IPEndPoint(resolved[0], defaultPort));
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

        if (!int.TryParse(server.AsSpan(closingBracketIndex + 2), System.Globalization.CultureInfo.InvariantCulture, out port))
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
