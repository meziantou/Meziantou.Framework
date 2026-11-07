using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// An NTP server that responds to NTP time queries.
/// Supports NTPv3 and NTPv4, mirroring the version sent by the client.
/// </summary>
/// <remarks>
/// The server answers from <see cref="NtpServerOptions.TimeProvider"/> and does not discipline that
/// clock, so it is only as accurate as the machine it runs on. It implements no authentication:
/// neither NTS (RFC 8915) nor symmetric keys.
/// </remarks>
public sealed class NtpServer : IDisposable
{
    private const int PacketSize = 48;
    private const int RootDispersionOffset = 8;
    private const int ReferenceIdentifierOffset = 12;
    private const int ReferenceTimestampOffset = 16;
    private const int OriginateTimestampOffset = 24;
    private const int ReceiveTimestampOffset = 32;
    private const int TransmitTimestampOffset = 40;

    private const byte KissOfDeathStratum = 0;

    /// <summary>The <c>RATE</c> Kiss-o'-Death code, telling a client it is being rate limited.</summary>
    private const uint RateKissCode = 0x52415445;

    /// <summary>Precision of ~1 microsecond, in log2 seconds.</summary>
    private const sbyte ClockPrecision = -20;

    private readonly NtpServerOptions _options;
    private readonly uint _referenceIdentifier;
    private readonly NtpRateLimiter? _rateLimiter;
    private readonly Lock _lock = new();

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtpServer"/> class.
    /// </summary>
    /// <param name="options">Optional server configuration.</param>
    public NtpServer(NtpServerOptions? options = null)
    {
        _options = options ?? new NtpServerOptions();
        _referenceIdentifier = EncodeReferenceIdentifier(_options.ReferenceIdentifier);

        // Rate limiting deliberately runs off the real clock, not the configured TimeProvider: a
        // simulated clock must not be able to switch the protection off or freeze its window.
        _rateLimiter = _options.MaxRequestsPerSecond > 0
            ? new NtpRateLimiter(_options.MaxRequestsPerSecond, TimeProvider.System)
            : null;
    }

    /// <summary>Gets the port the server is listening on. Only valid after <see cref="StartAsync"/> has been called.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Gets a task that completes when the listen loop stops, either because the server was disposed or
    /// the start token was cancelled, or faulted because the loop hit an unrecoverable error.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server has not been started.</exception>
    public Task Completion => _listenTask ?? throw new InvalidOperationException($"The server has not been started; call {nameof(StartAsync)} first.");

    /// <summary>Starts the NTP server and begins listening for client requests.</summary>
    /// <param name="cancellationToken">A cancellation token that stops the server when cancelled.</param>
    /// <returns>A task that completes when the server has started listening.</returns>
    /// <exception cref="InvalidOperationException">The server has already been started.</exception>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_listenTask is not null)
                throw new InvalidOperationException("The server has already been started.");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var udpClient = new UdpClient(new IPEndPoint(_options.BindAddress, _options.Port));
            _udpClient = udpClient;
            Port = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;

            _listenTask = ListenAsync(udpClient, _cts.Token);
        }

        return Task.CompletedTask;
    }

    /// <summary>Releases the resources used by this instance and stops listening.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _cts?.Cancel();
        _udpClient?.Dispose();
        _cts?.Dispose();
    }

    private async Task ListenAsync(UdpClient udpClient, CancellationToken cancellationToken)
    {
        var consecutiveErrors = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // A socket error is about one datagram, not about the listener. On Windows in
                // particular, a client that closes its socket before our reply arrives causes an ICMP
                // port-unreachable that surfaces here as ConnectionReset on the *next* receive; letting
                // it escape would stop the server for good because of an unrelated peer.
                using var errorActivity = NtpServerTelemetry.ActivitySource.StartActivity("ntp.server.receive_error");
                errorActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                if (++consecutiveErrors > 10)
                {
                    // Back off rather than spin if the socket is failing every time.
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                continue;
            }

            consecutiveErrors = 0;
            ProcessRequest(udpClient, result.Buffer, result.RemoteEndPoint);
        }
    }

    private void ProcessRequest(UdpClient udpClient, byte[] requestData, IPEndPoint remoteEndPoint)
    {
        using var activity = NtpServerTelemetry.ActivitySource.StartActivity("ntp.server.request");
        activity?.SetTag("ntp.client.address", remoteEndPoint.Address.ToString());

        try
        {
            if (requestData.Length < PacketSize)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Packet too small");
                return;
            }

            var requestFirstByte = requestData[0];
            var version = (requestFirstByte >> 3) & 0x07;
            var mode = (NtpMode)(requestFirstByte & 0x07);
            var pollInterval = requestData[2];

            activity?.SetTag("ntp.version", version);

            // Only answer client-mode requests. Answering a server-mode packet is what lets two NTP
            // servers pointed at each other exchange packets forever, and answering anything else
            // turns the server into a reflector for traffic it has no reason to acknowledge.
            if (mode is not NtpMode.Client)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"Unexpected mode {(int)mode}");
                return;
            }

            if (version is not (3 or 4))
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"Unsupported version {version}");
                return;
            }

            if (_rateLimiter is not null && !_rateLimiter.TryAcquire(remoteEndPoint.Address, out var isFirstRejection))
            {
                activity?.SetTag("ntp.rate_limited", value: true);
                activity?.SetStatus(ActivityStatusCode.Error, "Rate limited");

                // Answer at most one Kiss-o'-Death per window. Replying to every throttled packet would
                // make the server amplify exactly the flood it is trying to shed.
                if (isFirstRejection)
                    SendKissOfDeath(udpClient, requestData, remoteEndPoint, version);

                return;
            }

            var now = _options.TimeProvider.GetUtcNow();

            var responseBuffer = new byte[PacketSize];

            // Byte 0: LeapIndicator (0=NoWarning) | Version (mirrored) | Mode (4=Server)
            responseBuffer[0] = (byte)((version << 3) | (int)NtpMode.Server);
            responseBuffer[1] = _options.Stratum;
            responseBuffer[2] = pollInterval;
            responseBuffer[3] = unchecked((byte)ClockPrecision);

            // Root delay stays zero: there is no network path to a reference clock to account for.
            NtpTimestamp.EncodeShortFormat(_options.RootDispersion, responseBuffer.AsSpan(RootDispersionOffset, 4));
            BinaryPrimitives.WriteUInt32BigEndian(responseBuffer.AsSpan(ReferenceIdentifierOffset, 4), _referenceIdentifier);

            // Copy the client's transmit timestamp to the originate timestamp, byte for byte. This is
            // what lets the client tie the reply to its request, so it must survive verbatim.
            requestData.AsSpan(TransmitTimestampOffset, NtpTimestamp.Size).CopyTo(responseBuffer.AsSpan(OriginateTimestampOffset, NtpTimestamp.Size));

            NtpTimestamp.Encode(now, responseBuffer.AsSpan(ReferenceTimestampOffset));
            NtpTimestamp.Encode(now, responseBuffer.AsSpan(ReceiveTimestampOffset));
            NtpTimestamp.Encode(_options.TimeProvider.GetUtcNow(), responseBuffer.AsSpan(TransmitTimestampOffset));

            udpClient.Send(responseBuffer, responseBuffer.Length, remoteEndPoint);

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
    }

    private static void SendKissOfDeath(UdpClient udpClient, byte[] requestData, IPEndPoint remoteEndPoint, int version)
    {
        var buffer = new byte[PacketSize];

        buffer[0] = (byte)((version << 3) | (int)NtpMode.Server);
        buffer[1] = KissOfDeathStratum;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(ReferenceIdentifierOffset, 4), RateKissCode);
        requestData.AsSpan(TransmitTimestampOffset, NtpTimestamp.Size).CopyTo(buffer.AsSpan(OriginateTimestampOffset, NtpTimestamp.Size));

        // The timestamps stay zero: a Kiss-o'-Death packet carries no time, only a reason.
        udpClient.Send(buffer, buffer.Length, remoteEndPoint);
    }

    private static uint EncodeReferenceIdentifier(string referenceIdentifier)
    {
        ArgumentNullException.ThrowIfNull(referenceIdentifier);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(referenceIdentifier.Length, 4, nameof(referenceIdentifier));

        uint value = 0;
        for (var i = 0; i < 4; i++)
        {
            var c = i < referenceIdentifier.Length ? referenceIdentifier[i] : '\0';
            if (c > 0x7F)
                throw new ArgumentException($"The reference identifier must be ASCII, but contains '{c}'.", nameof(referenceIdentifier));

            value |= (uint)c << ((3 - i) * 8);
        }

        return value;
    }
}
