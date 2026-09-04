using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// An NTP client for querying NTP servers to retrieve accurate network time.
/// Supports NTPv3 and NTPv4.
/// </summary>
/// <remarks>
/// Responses are validated as described on <see cref="NtpClientOptions.ValidateResponse"/>, but they
/// are never authenticated cryptographically. An attacker who can inject packets on the path to the
/// server can still control the reported time.
/// </remarks>
public sealed class NtpClient
{
    private readonly string _server;
    private readonly NtpClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtpClient"/> class using default options (NTPv4, port 123).
    /// </summary>
    /// <param name="server">The NTP server hostname or IP address.</param>
    public NtpClient(string server)
        : this(server, options: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NtpClient"/> class.
    /// </summary>
    /// <param name="server">The NTP server hostname or IP address.</param>
    /// <param name="options">Optional configuration options.</param>
    public NtpClient(string server, NtpClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(server);

        _server = server;
        _options = options ?? NtpClientOptions.Default;
    }

    /// <summary>Queries the NTP server and returns the time response.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The NTP response containing timestamps and computed offset/delay.</returns>
    /// <exception cref="TimeoutException">Every resolved address timed out.</exception>
    /// <exception cref="AggregateException">Every resolved address failed, for differing reasons.</exception>
    public async Task<NtpResponse> QueryAsync(CancellationToken cancellationToken = default)
    {
        using var activity = NtpTelemetry.ActivitySource.StartActivity("ntp.query");
        activity?.SetTag("ntp.server", _server);
        activity?.SetTag("ntp.version", (int)_options.Version);

        try
        {
            var endpoints = await ResolveEndpointsAsync(cancellationToken).ConfigureAwait(false);

            List<Exception> exceptions = [];
            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await QueryEndpointAsync(endpoint, activity, cancellationToken).ConfigureAwait(false);

                    activity?.SetTag("ntp.stratum", response.Stratum);
                    activity?.SetTag("ntp.address_family", endpoint.AddressFamily.ToString());
                    activity?.SetStatus(ActivityStatusCode.Ok);

                    return response;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            throw CreateFailureException(exceptions);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private async Task<NtpResponse> QueryEndpointAsync(IPEndPoint endpoint, Activity? activity, CancellationToken cancellationToken)
    {
        // Every address gets its own timeout. Sharing one budget across the whole list lets a single
        // unresponsive address -- a AAAA record on a host with no IPv6 route, typically -- consume it
        // all, leaving no time for the address that would have answered.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);
        var timeoutToken = timeoutCts.Token;

        string? lastRejection = null;
        try
        {
            var request = NtpPacket.CreateClientRequest(_options.Version);
            request.TransmitTimestamp = DateTimeOffset.UtcNow;

            var requestBuffer = new byte[NtpPacket.PacketSize];
            request.Encode(requestBuffer);

            using var client = new UdpClient(endpoint.AddressFamily);

            // Connecting the socket makes the OS drop datagrams from every other source, so an
            // off-path attacker cannot race the real server by guessing only the source port.
            client.Connect(endpoint);
            await client.SendAsync(requestBuffer, timeoutToken).ConfigureAwait(false);

            while (true)
            {
                var result = await client.ReceiveAsync(timeoutToken).ConfigureAwait(false);
                var destinationTimestamp = DateTimeOffset.UtcNow;

                if (TryReadResponse(result.Buffer, requestBuffer, destinationTimestamp, out var response, out var rejection))
                    return response;

                // Keep waiting for a valid reply instead of failing, so a single stray or forged
                // datagram cannot deny service for the whole timeout.
                lastRejection = rejection;
                activity?.AddEvent(new ActivityEvent("ntp.response_rejected", tags: new ActivityTagsCollection
                {
                    { "ntp.rejection_reason", rejection },
                    { "ntp.peer.address", endpoint.ToString() },
                }));
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var detail = lastRejection is null ? "" : $" The last response was rejected because {lastRejection}.";
            throw new TimeoutException($"The NTP query to '{endpoint}' did not complete within {_options.Timeout}.{detail}", ex);
        }
    }

    private bool TryReadResponse(
        ReadOnlySpan<byte> buffer,
        ReadOnlySpan<byte> request,
        DateTimeOffset destinationTimestamp,
        [NotNullWhen(true)] out NtpResponse? response,
        [NotNullWhen(false)] out string? rejection)
    {
        response = null;

        if (buffer.Length < NtpPacket.PacketSize)
        {
            rejection = $"it is {buffer.Length} bytes, expected at least {NtpPacket.PacketSize}";
            return false;
        }

        var packet = NtpPacket.Decode(buffer);

        // These hold whatever NtpClientOptions.ValidateResponse says: without them the reply is not a
        // usable time reading, and the offset computed from it would be meaningless rather than merely
        // untrusted.
        if (packet.Mode is not NtpMode.Server)
        {
            rejection = $"its mode is {(int)packet.Mode}, expected {(int)NtpMode.Server} (server)";
            return false;
        }

        if (packet.OriginateTimestamp is null || packet.ReceiveTimestamp is null || packet.TransmitTimestamp is null)
        {
            rejection = "it is missing the originate, receive, or transmit timestamp";
            return false;
        }

        if (_options.ValidateResponse)
        {
            // RFC 5905 TEST2. The originate timestamp is the only thing binding a reply to this
            // request, so compare the raw bytes: decoding to DateTimeOffset is lossy and would leave
            // an attacker more room than the wire format does.
            var echoed = buffer.Slice(NtpPacket.OriginateTimestampOffset, NtpTimestamp.Size);
            var sent = request.Slice(NtpPacket.TransmitTimestampOffset, NtpTimestamp.Size);
            if (!echoed.SequenceEqual(sent))
            {
                rejection = "its originate timestamp does not echo the transmit timestamp of the request";
                return false;
            }

            if (packet.Version is not (NtpVersion.V3 or NtpVersion.V4))
            {
                rejection = $"its version is {(int)packet.Version}, expected 3 or 4";
                return false;
            }

            if (packet.Stratum is NtpPacket.KissOfDeathStratum)
            {
                var kissCode = NtpPacket.FormatReferenceIdentifier(packet.ReferenceIdentifier);
                rejection = kissCode is null
                    ? "the server returned a Kiss-o'-Death packet"
                    : $"the server returned a Kiss-o'-Death packet ({kissCode})";
                return false;
            }

            // RFC 5905 TEST3: the server is telling us its own clock is not synchronized.
            if (packet.LeapIndicator is NtpLeapIndicator.AlarmCondition)
            {
                rejection = "the server reports an alarm condition, meaning its clock is not synchronized";
                return false;
            }
        }

        response = new NtpResponse(packet, destinationTimestamp);
        rejection = null;

        return true;
    }

    private Exception CreateFailureException(List<Exception> exceptions)
    {
        // A single resolved address is the common case; report its own exception so the reason a
        // response was rejected survives instead of being flattened into a generic message.
        if (exceptions.Count is 1)
            return exceptions[0];

        // Surface a plain timeout as a TimeoutException rather than burying it in an AggregateException,
        // so that `catch (TimeoutException)` works for the case callers actually expect.
        if (exceptions.TrueForAll(static ex => ex is TimeoutException))
        {
            return new TimeoutException(
                $"The NTP query to '{_server}' timed out on all {exceptions.Count} resolved address(es), allowing {_options.Timeout} each.",
                exceptions[0]);
        }

        return new AggregateException($"Failed to query NTP server '{_server}' on all resolved addresses", exceptions);
    }

    private async ValueTask<IPEndPoint[]> ResolveEndpointsAsync(CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(_server, out var address))
            return [new IPEndPoint(address, _options.Port)];

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(_server, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Resolving host '{_server}' did not complete within {_options.Timeout}.", ex);
        }

        if (addresses.Length is 0)
            throw new InvalidOperationException($"Could not resolve host: {_server}");

        return Array.ConvertAll(addresses, a => new IPEndPoint(a, _options.Port));
    }
}
