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

    public abstract Task<Stream> AcceptAsync(CancellationToken cancellationToken);

    public abstract void Dispose();

    public static NodeJsEndpoint Create()
    {
        var name = "mfnodejs-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        if (OperatingSystem.IsWindows())
            return new NamedPipeEndpoint(name);

        return new UnixSocketEndpoint(name);
    }

    private sealed class NamedPipeEndpoint : NodeJsEndpoint
    {
        private readonly NamedPipeServerStream _server;
        private bool _accepted;

        public NamedPipeEndpoint(string name)
        {
            _server = new NamedPipeServerStream(name, PipeDirection.InOut, maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Address = @"\\.\pipe\" + name;
        }

        public override string Address { get; }

        public override async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
        {
            await _server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            _accepted = true;
            return _server;
        }

        public override void Dispose()
        {
            if (!_accepted)
            {
                _server.Dispose();
            }
        }
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class UnixSocketEndpoint : NodeJsEndpoint
    {
        // sockaddr_un.sun_path is 104 bytes on macOS and 108 bytes on Linux, including the null terminator
        private const int MaxSocketPathLength = 100;

        private readonly string _directory;
        private readonly Socket _listener;

        public UnixSocketEndpoint(string name)
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
                _listener.Listen(1);
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
            Dispose();
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
