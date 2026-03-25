using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.NtpServer;

/// <summary>
/// An NTP server that responds to NTP time queries.
/// Supports NTPv3 and NTPv4, mirroring the version sent by the client.
/// </summary>
[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace")]
public sealed class NtpServer : IDisposable
{
    private const int PacketSize = 48;
    private static readonly long NtpEpochTicks = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;

    private readonly NtpServerOptions _options;
    private UdpClient? _udpClient;
    private Task? _listenTask;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtpServer"/> class.
    /// </summary>
    /// <param name="options">Optional server configuration.</param>
    public NtpServer(NtpServerOptions? options = null)
    {
        _options = options ?? new NtpServerOptions();
    }

    /// <summary>Gets the port the server is listening on. Only valid after <see cref="StartAsync"/> has been called.</summary>
    public int Port { get; private set; }

    /// <summary>Starts the NTP server and begins listening for client requests.</summary>
    /// <param name="cancellationToken">A cancellation token that stops the server when cancelled.</param>
    /// <returns>A task that completes when the server has started listening.</returns>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_cts is not null && _cts.IsCancellationRequested, this);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, _options.Port));
        Port = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

        _listenTask = ListenAsync(_cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>Releases the resources used by this instance and stops listening.</summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
        _cts?.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                ProcessRequest(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void ProcessRequest(byte[] requestData, IPEndPoint remoteEndPoint)
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
            var pollInterval = requestData[2];

            var now = _options.TimeProvider.GetUtcNow();

            var responseBuffer = new byte[PacketSize];

            // Byte 0: LeapIndicator (0=NoWarning) | Version (mirrored) | Mode (4=Server)
            responseBuffer[0] = (byte)((version << 3) | 4);
            responseBuffer[1] = _options.Stratum;
            responseBuffer[2] = pollInterval;
            responseBuffer[3] = unchecked((byte)-20); // Precision: ~1 microsecond

            // Copy client's transmit timestamp (bytes 40-47) to originate timestamp (bytes 24-31)
            requestData.AsSpan(40, 8).CopyTo(responseBuffer.AsSpan(24, 8));

            EncodeTimestamp(now, responseBuffer.AsSpan(16)); // Reference timestamp
            EncodeTimestamp(now, responseBuffer.AsSpan(32)); // Receive timestamp
            EncodeTimestamp(_options.TimeProvider.GetUtcNow(), responseBuffer.AsSpan(40)); // Transmit timestamp

            _udpClient!.Send(responseBuffer, responseBuffer.Length, remoteEndPoint);

            activity?.SetTag("ntp.version", version);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
    }

    private static void EncodeTimestamp(DateTimeOffset value, Span<byte> buffer)
    {
        var ticks = value.UtcTicks - NtpEpochTicks;
        var seconds = (uint)(ticks / TimeSpan.TicksPerSecond);
        var remainingTicks = ticks % TimeSpan.TicksPerSecond;
        var fraction = (uint)(remainingTicks * 0x1_0000_0000L / TimeSpan.TicksPerSecond);

        BinaryPrimitives.WriteUInt32BigEndian(buffer, seconds);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[4..], fraction);
    }
}
