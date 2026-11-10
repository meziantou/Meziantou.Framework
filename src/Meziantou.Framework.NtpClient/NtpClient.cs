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
/// Responses are validated as described on <see cref="NtpClientOptions.ValidateResponse"/>. They are
/// authenticated only when <see cref="NtpClientOptions.AuthenticationKey"/> is set: otherwise, an
/// attacker who can inject packets on the path to the server can still control the reported time.
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

    /// <summary>Gets the host name or the IP address of the server.</summary>
    public string Server => _server;

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

    /// <summary>
    /// Queries the NTP server several times and keeps the sample with the shortest round-trip delay, whose offset is
    /// the most accurate, as the clock filter of RFC 5905 does.
    /// </summary>
    /// <param name="options">The number of samples and the delay between them. Default is 4 samples, 2 seconds apart.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The samples, the best one, and the jitter and root distance of the measurement.</returns>
    /// <exception cref="TimeoutException">No query got a valid reply in time.</exception>
    /// <exception cref="AggregateException">No query got a valid reply, for differing reasons.</exception>
    public async Task<NtpServerMeasurement> MeasureAsync(NtpMeasurementOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= NtpMeasurementOptions.Default;
        var sampleCount = options.SampleCount;
        var sampleInterval = options.SampleInterval;

        List<NtpResponse> samples = [];
        List<Exception> errors = [];
        for (var i = 0; i < sampleCount; i++)
        {
            if (i > 0 && sampleInterval > TimeSpan.Zero)
            {
                await Task.Delay(sampleInterval, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                samples.Add(await QueryAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        if (samples.Count is 0)
            throw CreateFailureException(errors, $"None of the {sampleCount} queries to NTP server '{_server}' got a valid reply");

        return new NtpServerMeasurement(this, samples, errors);
    }

    /// <summary>
    /// Queries several NTP servers, discards the ones whose time disagrees with the majority, and combines the others, as
    /// the selection and combine algorithms of RFC 5905 do.
    /// </summary>
    /// <param name="clients">
    /// One client per server, each with its own options. Query at least 3 servers, so that a majority can outvote one
    /// that is wrong, and do not query the same server twice: each client is one vote.
    /// </param>
    /// <param name="options">The number of samples per server and the delay between them. Default is 4 samples, 2 seconds apart.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The combined clock offset, its bounds, and the measurement of each server.</returns>
    /// <remarks>The servers are queried concurrently. A server that does not answer is listed in <see cref="NtpClockMeasurement.FailedServers"/>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="clients"/> is empty or contains <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">No server gave a valid reply in time.</exception>
    /// <exception cref="AggregateException">No server gave a valid reply, for differing reasons.</exception>
    /// <exception cref="NtpClockSelectionException">Servers answered, but no majority of them agrees on the time.</exception>
    public static async Task<NtpClockMeasurement> MeasureAsync(IEnumerable<NtpClient> clients, NtpMeasurementOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clients);

        List<NtpClient> clientList = [];
        foreach (var client in clients)
        {
            if (client is null)
                throw new ArgumentException("The list of clients cannot contain null.", nameof(clients));

            // The same instance twice would give its server two votes
            if (!clientList.Contains(client))
            {
                clientList.Add(client);
            }
        }

        if (clientList.Count is 0)
            throw new ArgumentException("The list of clients cannot be empty.", nameof(clients));

        var tasks = clientList.ConvertAll(client => client.MeasureAsync(options, cancellationToken));
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Each failure is read from its task below
        }

        // A server that failed before the caller canceled makes Task.WhenAll throw its fault instead of the cancellation
        cancellationToken.ThrowIfCancellationRequested();

        List<NtpServerMeasurement> measurements = [];
        var failedServers = new Dictionary<NtpClient, Exception>();
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (task.IsCompletedSuccessfully)
            {
                measurements.Add(task.Result);
            }
            else
            {
                failedServers.Add(clientList[i], task.Exception?.InnerExceptions is [var single] ? single : (Exception?)task.Exception ?? new OperationCanceledException());
            }
        }

        if (measurements.Count is 0)
            throw CreateFailureException([.. failedServers.Values], "None of the NTP servers gave a valid reply");

        var offsets = new long[measurements.Count];
        var rootDistances = new long[measurements.Count];
        for (var i = 0; i < measurements.Count; i++)
        {
            offsets[i] = measurements[i].ClockOffset.Ticks;
            rootDistances[i] = measurements[i].RootDistance.Ticks;
        }

        var selected = new bool[measurements.Count];
        if (!NtpClockSelection.TrySelect(offsets, rootDistances, selected, out var low, out var high))
            throw new NtpClockSelectionException($"No majority of the {measurements.Count} NTP servers that answered agrees on the time.", measurements, failedServers);

        var (offset, jitter) = NtpClockSelection.Combine(offsets, rootDistances, selected);

        List<NtpServerMeasurement> selectedServers = [];
        List<NtpServerMeasurement> rejectedServers = [];
        for (var i = 0; i < measurements.Count; i++)
        {
            (selected[i] ? selectedServers : rejectedServers).Add(measurements[i]);
        }

        return new NtpClockMeasurement(
            TimeSpan.FromTicks(offset),
            TimeSpan.FromTicks(jitter),
            TimeSpan.FromTicks(low),
            TimeSpan.FromTicks(high),
            selectedServers,
            rejectedServers,
            failedServers);
    }

    private async Task<NtpResponse> QueryEndpointAsync(IPEndPoint endpoint, Activity? activity, CancellationToken cancellationToken)
    {
        using var client = new UdpClient(endpoint.AddressFamily);

        // Connecting the socket makes the OS drop datagrams from every other source, so an
        // off-path attacker cannot race the real server by guessing only the source port.
        client.Connect(endpoint);

        var attemptCount = _options.RetryCount + 1;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await QueryEndpointOnceAsync(client, endpoint, activity, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException) when (attempt < attemptCount)
            {
                // A lost request or reply: send a new request. A late reply to the previous one does not echo the new
                // transmit timestamp, so it is ignored.
                activity?.AddEvent(new ActivityEvent("ntp.retry", tags: new ActivityTagsCollection
                {
                    { "ntp.peer.address", endpoint.ToString() },
                    { "ntp.attempt", attempt + 1 },
                }));
            }
            catch (TimeoutException ex) when (attemptCount > 1)
            {
                throw new TimeoutException($"The NTP query to '{endpoint}' got no valid reply to {attemptCount} attempts, allowing {_options.Timeout} each. {ex.Message}", ex);
            }
        }
    }

    private async Task<NtpResponse> QueryEndpointOnceAsync(UdpClient client, IPEndPoint endpoint, Activity? activity, CancellationToken cancellationToken)
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
            var requestBuffer = CreateRequest(_options, DateTimeOffset.UtcNow);
            await client.SendAsync(requestBuffer, timeoutToken).ConfigureAwait(false);

            while (true)
            {
                var result = await client.ReceiveAsync(timeoutToken).ConfigureAwait(false);
                var destinationTimestamp = DateTimeOffset.UtcNow;

                if (TryReadResponse(_options, result.Buffer, requestBuffer, destinationTimestamp, out var response, out var rejection))
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

    /// <summary>Creates a client request, followed by its MAC when <see cref="NtpClientOptions.AuthenticationKey"/> is set.</summary>
    internal static byte[] CreateRequest(NtpClientOptions options, DateTimeOffset transmitTimestamp)
    {
        var request = NtpPacket.CreateClientRequest(options.Version);
        request.TransmitTimestamp = transmitTimestamp;

        var key = options.AuthenticationKey;
        var buffer = new byte[NtpPacket.PacketSize + (key is null ? 0 : NtpMac.GetMacSize(key))];
        request.Encode(buffer);
        if (key is not null)
        {
            NtpMac.Write(key, buffer, NtpPacket.PacketSize);
        }

        return buffer;
    }

    /// <summary>Checks that <paramref name="buffer"/> is a valid reply to <paramref name="request"/>, and decodes it.</summary>
    internal static bool TryReadResponse(
        NtpClientOptions options,
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

        var key = options.AuthenticationKey;
        if (key is not null)
        {
            // Authenticate first: nothing else in an unauthenticated reply can be trusted, not even why it is invalid
            if (!NtpMac.TryGetMacOffset(buffer, out var macOffset))
            {
                rejection = "its extension fields or its MAC are malformed";
                return false;
            }

            var macLength = buffer.Length - macOffset;
            if (macLength is 0)
            {
                rejection = "it is not authenticated";
                return false;
            }

            if (macLength is NtpMac.CryptoNakSize)
            {
                rejection = "the server could not authenticate the request (crypto-NAK); check the key identifier, the algorithm and the key";
                return false;
            }

            if (!NtpMac.Verify(key, buffer, macOffset))
            {
                rejection = "its MAC is not valid for the authentication key";
                return false;
            }
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

        if (options.ValidateResponse)
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
        // Surface a plain timeout as a TimeoutException rather than burying it in an AggregateException,
        // so that `catch (TimeoutException)` works for the case callers actually expect.
        if (exceptions.Count > 1 && exceptions.TrueForAll(ex => ex is TimeoutException))
        {
            return new TimeoutException(
                $"The NTP query to '{_server}' timed out on all {exceptions.Count} resolved address(es), allowing {_options.Timeout} each.",
                exceptions[0]);
        }

        return CreateFailureException(exceptions, $"Failed to query NTP server '{_server}' on all resolved addresses");
    }

    private static Exception CreateFailureException(List<Exception> exceptions, string message)
    {
        // A single failure is the common case; report its own exception so the reason a response was
        // rejected survives instead of being flattened into a generic message.
        if (exceptions.Count is 1)
            return exceptions[0];

        if (exceptions.TrueForAll(ex => ex is TimeoutException))
            return new TimeoutException(message + ": every query timed out.", exceptions[0]);

        return new AggregateException(message, exceptions);
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
