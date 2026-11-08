using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds;

/// <summary>Answers the SQL Server Resolution Protocol (MC-SQLR) requests on a UDP socket.</summary>
internal sealed class TdsSqlServerBrowserResponder : IDisposable
{
    // MC-SQLR 2.2: request and response message types
    internal const byte ClientBroadcastEx = 0x02;
    internal const byte ClientUnicastEx = 0x03;
    internal const byte ClientUnicastInstance = 0x04;
    internal const byte ServerResponse = 0x05;

    // MC-SQLR 2.2.2: the instance name of CLNT_UCAST_INST is at most 32 bytes, followed by a 0x00 terminator
    private const int MaxInstanceNameLength = 32;

    // MC-SQLR 2.2.5: RESP_DATA is at most 1024 bytes for CLNT_UCAST_INST, and 65535 bytes for the other requests
    private const int MaxInstanceResponseDataLength = 1024;

    private readonly UdpClient _udpClient;
    private readonly string _instanceName;
    private readonly string _serverName;
    private readonly Func<int?> _getTcpPort;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    private TdsSqlServerBrowserResponder(UdpClient udpClient, TdsSqlServerBrowserOptions options, Func<int?> getTcpPort, ILogger logger)
    {
        _udpClient = udpClient;
        _instanceName = options.InstanceName;
        _serverName = options.ServerName ?? Environment.MachineName;
        _getTcpPort = getTcpPort;
        _logger = logger;
    }

    public int Port => ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

    /// <summary>Binds the UDP socket and starts answering requests.</summary>
    /// <param name="options">The responder options.</param>
    /// <param name="getTcpPort">Returns the TCP port to advertise, or <see langword="null"/> when the server is not listening yet.</param>
    /// <param name="logger">The logger.</param>
    public static TdsSqlServerBrowserResponder Start(TdsSqlServerBrowserOptions options, Func<int?> getTcpPort, ILogger logger)
    {
        Validate(options);

        var udpClient = new UdpClient(options.BindAddress.AddressFamily);
        try
        {
            udpClient.Client.Bind(new IPEndPoint(options.BindAddress, options.Port));
        }
        catch
        {
            udpClient.Dispose();
            throw;
        }

        var responder = new TdsSqlServerBrowserResponder(udpClient, options, getTcpPort, logger);
        _ = responder.ReceiveLoopAsync(responder._cts.Token);
        return responder;
    }

    internal static void Validate(TdsSqlServerBrowserOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.InstanceName, nameof(options) + "." + nameof(options.InstanceName));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, IPEndPoint.MinPort, nameof(options) + "." + nameof(options.Port));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, IPEndPoint.MaxPort, nameof(options) + "." + nameof(options.Port));
        if (options.InstanceName.Contains(';', StringComparison.Ordinal) || (options.ServerName?.Contains(';', StringComparison.Ordinal) ?? false))
            throw new ArgumentException("The instance name and the server name cannot contain a semicolon.", nameof(options));

        if (Encoding.ASCII.GetByteCount(options.InstanceName) > MaxInstanceNameLength)
            throw new ArgumentException($"The instance name cannot be longer than {MaxInstanceNameLength} characters.", nameof(options));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udpClient.Dispose();
        _cts.Dispose();
    }

    /// <summary>Builds the SVR_RESP message answering a request, or returns <see langword="null"/> when the request gets no answer.</summary>
    internal static byte[]? CreateResponse(ReadOnlySpan<byte> request, string serverName, string instanceName, int tcpPort)
    {
        if (request.IsEmpty)
            return null;

        switch (request[0])
        {
            case ClientBroadcastEx or ClientUnicastEx:
                return CreateServerResponse(serverName, instanceName, tcpPort);

            case ClientUnicastInstance:
                // The instance name is a null-terminated string in the client's code page
                var name = request[1..];
                var terminator = name.IndexOf((byte)0);
                if (terminator >= 0)
                {
                    name = name[..terminator];
                }

                if (name.Length is 0 or > MaxInstanceNameLength)
                    return null;

                if (!string.Equals(Encoding.ASCII.GetString(name), instanceName, StringComparison.OrdinalIgnoreCase))
                    return null;

                var response = CreateServerResponse(serverName, instanceName, tcpPort);
                return response.Length - 3 <= MaxInstanceResponseDataLength ? response : null;

            default:
                // CLNT_UCAST_DAC (0x0F) asks for the dedicated admin connection, which the server does not provide
                return null;
        }
    }

    private static byte[] CreateServerResponse(string serverName, string instanceName, int tcpPort)
    {
        // MC-SQLR 2.2.5: SVR_RESP = 0x05, RESP_SIZE (little-endian ushort), RESP_DATA (MBCS string)
        var data = string.Create(CultureInfo.InvariantCulture, $"ServerName;{serverName};InstanceName;{instanceName};IsClustered;No;Version;1.0.0.0;tcp;{tcpPort};;");
        var dataLength = Encoding.ASCII.GetByteCount(data);
        var response = new byte[3 + dataLength];
        response[0] = ServerResponse;
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(1), checked((ushort)dataLength));
        Encoding.ASCII.GetBytes(data, response.AsSpan(3));
        return response;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult request;
            try
            {
                request = await _udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset)
            {
                // Windows reports an ICMP "port unreachable" from a previous send as a receive error
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stopped answering SQL Server Browser requests after a socket failure");
                return;
            }

            var tcpPort = _getTcpPort();
            if (tcpPort is null)
                continue;

            var response = CreateResponse(request.Buffer, _serverName, _instanceName, tcpPort.Value);
            if (response is null)
                continue;

            try
            {
                await _udpClient.SendAsync(response, request.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Cannot send the SQL Server Browser response to {RemoteEndPoint}", request.RemoteEndPoint);
            }
        }
    }
}
