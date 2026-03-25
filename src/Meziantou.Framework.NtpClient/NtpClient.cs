using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.NtpClient;

/// <summary>
/// An NTP client for querying NTP servers to retrieve accurate network time.
/// Supports NTPv3 and NTPv4.
/// </summary>
[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace")]
public sealed class NtpClient : IDisposable
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
    public async Task<NtpResponse> QueryAsync(CancellationToken cancellationToken = default)
    {
        using var activity = NtpTelemetry.ActivitySource.StartActivity("ntp.query");
        activity?.SetTag("ntp.server", _server);
        activity?.SetTag("ntp.version", (int)_options.Version);

        try
        {
            var endpoint = await ResolveEndpointAsync(cancellationToken).ConfigureAwait(false);

            var request = NtpPacket.CreateClientRequest(_options.Version);
            request.TransmitTimestamp = DateTimeOffset.UtcNow;

            var requestBuffer = new byte[NtpPacket.PacketSize];
            request.Encode(requestBuffer);

            using var client = new UdpClient(endpoint.AddressFamily);
            await client.SendAsync(requestBuffer, requestBuffer.Length, endpoint).ConfigureAwait(false);

            var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var destinationTimestamp = DateTimeOffset.UtcNow;

            var responsePacket = NtpPacket.Decode(result.Buffer);

            activity?.SetTag("ntp.stratum", responsePacket.Stratum);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return new NtpResponse(responsePacket, destinationTimestamp);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>Releases the resources used by this instance.</summary>
    public void Dispose()
    {
        // No persistent resources to dispose; UdpClient is created per-query.
    }

    private async ValueTask<IPEndPoint> ResolveEndpointAsync(CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(_server, out var address))
            return new IPEndPoint(address, _options.Port);

        var addresses = await Dns.GetHostAddressesAsync(_server, cancellationToken).ConfigureAwait(false);
        if (addresses.Length is 0)
            throw new InvalidOperationException($"Could not resolve host: {_server}");

        return new IPEndPoint(addresses[0], _options.Port);
    }
}
