using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>Private local endpoint the Node.js process connects to: a named pipe on Windows, a Unix domain socket elsewhere.</summary>
internal abstract class NodeJsEndpoint : IDisposable
{
    /// <summary>Gets the address passed to <c>net.connect</c> in the Node.js process.</summary>
    public abstract string Address { get; }

    /// <summary>Accepts the next connection. The caller owns the returned stream.</summary>
    public abstract Task<Stream> AcceptAsync(CancellationToken cancellationToken);

    public abstract void Dispose();

    /// <summary>Creates an endpoint that accepts up to <paramref name="maxConnections"/> connections, until it is disposed.</summary>
    public static NodeJsEndpoint Create(int maxConnections)
    {
        var name = "mfnodejs-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        if (OperatingSystem.IsWindows())
            return new NamedPipeEndpoint(name, maxConnections);

        return new UnixSocketEndpoint(name, maxConnections);
    }

    private sealed class NamedPipeEndpoint : NodeJsEndpoint
    {
        private readonly string _name;
        private readonly int _maxConnections;
        private NamedPipeServerStream? _server;
        private int _acceptedConnections;

        public NamedPipeEndpoint(string name, int maxConnections)
        {
            _name = name;
            _maxConnections = maxConnections;
            _server = CreateServer();
            Address = @"\\.\pipe\" + name;
        }

        public override string Address { get; }

        public override async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            var server = _server ?? throw new InvalidOperationException("The endpoint does not accept more connections.");
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

            // The next instance is created before the host asks the process to connect again, as a client cannot connect when no instance is waiting
            _acceptedConnections++;
            _server = _acceptedConnections < _maxConnections ? CreateServer() : null;
            return server;
        }

        public override void Dispose()
        {
            _server?.Dispose();
            _server = null;
        }

        private NamedPipeServerStream CreateServer()
        {
            return new NamedPipeServerStream(_name, PipeDirection.InOut, _maxConnections, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class UnixSocketEndpoint : NodeJsEndpoint
    {
        // sockaddr_un.sun_path is 104 bytes on macOS and 108 bytes on Linux, including the null terminator
        private const int MaxSocketPathLength = 100;

        private readonly string _directory;
        private readonly Socket _listener;

        public UnixSocketEndpoint(string name, int maxConnections)
        {
            var root = Path.GetTempPath();
            if (Path.Combine(root, name, "s").Length > MaxSocketPathLength)
            {
                root = "/tmp";
            }

            // Only the current user can access the directory, so other users cannot connect to the socket
            _directory = Path.Combine(root, name);
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Address = Path.Combine(_directory, "s");

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                _listener.Bind(new UnixDomainSocketEndPoint(Address));
                _listener.Listen(maxConnections);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public override string Address { get; }

        public override async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            var socket = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }

        public override void Dispose()
        {
            _listener.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
