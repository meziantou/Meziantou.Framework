using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

internal sealed class LoopbackHttpServer : IDisposable
{
    private const string Response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private int _acceptedConnectionCount;

    public LoopbackHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, port: 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptLoopAsync(_cancellationTokenSource.Token));
    }

    public int Port { get; }

    public int AcceptedConnectionCount => Volatile.Read(ref _acceptedConnectionCount);

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _acceptedConnectionCount);

                using var stream = client.GetStream();
                await ReadRequestHeadersAsync(stream, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Response), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task ReadRequestHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            count += read;
            if (Encoding.ASCII.GetString(buffer, 0, count).Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _listener.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
