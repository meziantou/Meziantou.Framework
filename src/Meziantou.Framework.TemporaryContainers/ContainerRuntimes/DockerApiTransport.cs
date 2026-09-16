using System.IO.Pipes;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerApiTransport
{
    internal sealed class Endpoint
    {
        private Endpoint(Uri baseAddress)
        {
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }
        public FullPath? UnixSocketPath { get; private set; }
        public string? NamedPipeServer { get; private set; }
        public string? NamedPipeName { get; private set; }
        public TlsSettings? Tls { get; private set; }
        public string DisplayName { get; private set; } = "";

        public static Endpoint ForHttp(Uri baseAddress, TlsSettings? tls = null)
        {
            return new Endpoint(baseAddress)
            {
                Tls = tls,
                DisplayName = baseAddress.ToString(),
            };
        }

        public static Endpoint ForUnixSocket(FullPath socketPath)
        {
            return new Endpoint(new Uri("http://localhost"))
            {
                UnixSocketPath = socketPath,
                DisplayName = "unix://" + socketPath.Value,
            };
        }

        public static Endpoint ForNamedPipe(string server, string name)
        {
            return new Endpoint(new Uri("http://localhost"))
            {
                NamedPipeServer = server,
                NamedPipeName = name,
                DisplayName = @"npipe://\\" + server + @"\pipe\" + name,
            };
        }
    }

    /// <summary>The TLS configuration of a daemon reached over TCP, read from the variables the docker CLI reads.</summary>
    /// <param name="CertificateDirectory">The directory holding <c>ca.pem</c>, <c>cert.pem</c> and <c>key.pem</c>.</param>
    internal sealed record TlsSettings(string CertificateDirectory);

    public static IEnumerable<Endpoint> GetEndpoints()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            if (TryParseDockerHost(dockerHost, GetTlsSettings(), out var endpoint))
                yield return endpoint;

            yield break;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            yield return Endpoint.ForUnixSocket(FullPath.FromPath("/var/run/docker.sock"));

        if (OperatingSystem.IsLinux() && TryGetLinuxRootlessSocket(out var rootlessSocket))
            yield return Endpoint.ForUnixSocket(rootlessSocket);

        if (OperatingSystem.IsWindows())
            yield return Endpoint.ForNamedPipe(".", "docker_engine");
    }

    /// <summary>Reads the TLS configuration of the docker CLI: TLS is used when <c>DOCKER_TLS_VERIFY</c> or <c>DOCKER_TLS</c> is set, with the certificates of <c>DOCKER_CERT_PATH</c> (<c>~/.docker</c> by default).</summary>
    private static TlsSettings? GetTlsSettings()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_TLS")))
            return null;

        var certificateDirectory = Environment.GetEnvironmentVariable("DOCKER_CERT_PATH");
        if (string.IsNullOrWhiteSpace(certificateDirectory))
            certificateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");

        return new TlsSettings(certificateDirectory);
    }

    public static HttpClient CreateClient(Endpoint endpoint)
    {
        SocketsHttpHandler? handler = null;
        try
        {
            handler = new SocketsHttpHandler();
            if (endpoint.UnixSocketPath is { } unixSocketPath)
            {
                handler.ConnectCallback = async (context, cancellationToken) => await ConnectUnixSocketAsync(unixSocketPath, cancellationToken).ConfigureAwait(false);
            }
            else if (endpoint.NamedPipeName is { } namedPipeName)
            {
                var server = endpoint.NamedPipeServer ?? ".";
                handler.ConnectCallback = async (context, cancellationToken) =>
                {
                    var stream = new NamedPipeClientStream(server, namedPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return stream;
                };
            }
            else if (endpoint.Tls is { } tls)
            {
                handler.SslOptions = CreateSslOptions(endpoint.BaseAddress.Host, tls);
            }

            var client = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = endpoint.BaseAddress,
                Timeout = Timeout.InfiniteTimeSpan,
            };

            handler = null;
            return client;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    private static async Task<NetworkStream> ConnectUnixSocketAsync(FullPath path, CancellationToken cancellationToken)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: true);
            socket = null;
            return stream;
        }
        finally
        {
            socket?.Dispose();
        }
    }

    private static SslClientAuthenticationOptions CreateSslOptions(string host, TlsSettings tls)
    {
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = host,
        };

        var certificatePath = Path.Combine(tls.CertificateDirectory, "cert.pem");
        var keyPath = Path.Combine(tls.CertificateDirectory, "key.pem");
        if (File.Exists(certificatePath) && File.Exists(keyPath))
        {
            using var pemCertificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

            // SChannel cannot use the ephemeral key of a certificate loaded from PEM files, so it goes through PKCS#12.
            options.ClientCertificates = [X509CertificateLoader.LoadPkcs12(pemCertificate.Export(X509ContentType.Pkcs12), password: null)];
        }

        // The daemon certificate is signed by the certificate authority of DOCKER_CERT_PATH, which the machine does not
        // trust on its own.
        var authorityPath = Path.Combine(tls.CertificateDirectory, "ca.pem");
        if (File.Exists(authorityPath))
        {
            var authority = X509CertificateLoader.LoadCertificateFromFile(authorityPath);
            options.RemoteCertificateValidationCallback = (_, certificate, _, errors) => IsTrustedByAuthority(certificate, errors, authority);
        }

        return options;
    }

    private static bool IsTrustedByAuthority(X509Certificate? certificate, SslPolicyErrors errors, X509Certificate2 authority)
    {
        if (errors is SslPolicyErrors.None)
            return true;

        // Only the chain is re-evaluated: a certificate whose name does not match the host is still rejected.
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var serverCertificate = new X509Certificate2(certificate);
        return chain.Build(serverCertificate);
    }

    /// <summary>Sends a request whose connection the daemon takes over, and returns that connection once the response headers are read. The standard input of an exec needs it: the process only sees the end of its input when the client closes its side of the connection, which an <see cref="HttpClient"/> cannot do.</summary>
    /// <returns>The connection, or the status and the body the daemon refused the request with.</returns>
    /// <exception cref="NotSupportedException">The daemon is reached over a named pipe, which cannot be half-closed.</exception>
    public static async Task<(UpgradedConnection? Connection, HttpStatusCode StatusCode, string ErrorBody)> SendUpgradeRequestAsync(Endpoint endpoint, string pathAndQuery, string jsonBody, CancellationToken cancellationToken)
    {
        if (endpoint.NamedPipeName is not null)
            throw new NotSupportedException("The Docker API runtime cannot send a standard input to a command over a named pipe. Use the 'docker' CLI runtime instead.");

        var connection = await ConnectRawAsync(endpoint, cancellationToken).ConfigureAwait(false);
        try
        {
            var body = Encoding.UTF8.GetBytes(jsonBody);
            var host = endpoint.UnixSocketPath is null ? endpoint.BaseAddress.Authority : "docker";
            var header = string.Create(CultureInfo.InvariantCulture, $"POST {pathAndQuery} HTTP/1.1\r\nHost: {host}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: Upgrade\r\nUpgrade: tcp\r\n\r\n");
            await connection.Stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
            await connection.Stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await connection.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var (statusCode, headers) = await ReadResponseHeadersAsync(connection.Stream, cancellationToken).ConfigureAwait(false);

            // The daemon answers '101 Switching Protocols' to a client that asks for the upgrade, and '200' with a raw
            // stream to one that does not; both hand the connection over.
            if (statusCode is 101 or 200)
                return (connection, (HttpStatusCode)statusCode, "");

            var errorBody = await ReadBodyAsync(connection.Stream, headers, cancellationToken).ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            return (null, (HttpStatusCode)statusCode, errorBody);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<UpgradedConnection> ConnectRawAsync(Endpoint endpoint, CancellationToken cancellationToken)
    {
        Socket? socket = null;
        try
        {
            if (endpoint.UnixSocketPath is { } unixSocketPath)
            {
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(unixSocketPath), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(endpoint.BaseAddress.DnsSafeHost, endpoint.BaseAddress.Port, cancellationToken).ConfigureAwait(false);
            }

            var networkStream = new NetworkStream(socket, ownsSocket: true);
            if (endpoint.Tls is not { } tls)
            {
                var plain = new UpgradedConnection(socket, networkStream, sslStream: null);
                socket = null;
                return plain;
            }

            var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
            try
            {
                await sslStream.AuthenticateAsClientAsync(CreateSslOptions(endpoint.BaseAddress.Host, tls), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
                socket = null;
                throw;
            }

            var secure = new UpgradedConnection(socket, sslStream, sslStream);
            socket = null;
            return secure;
        }
        finally
        {
            socket?.Dispose();
        }
    }

    /// <summary>Reads the status line and the headers, one byte at a time: everything after them belongs to the process, and must not end up in a read buffer.</summary>
    private static async Task<(int StatusCode, Dictionary<string, string> Headers)> ReadResponseHeadersAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(512);
        var single = new byte[1];
        while (buffer.Count < 4 || buffer[^4] != '\r' || buffer[^3] != '\n' || buffer[^2] != '\r' || buffer[^1] != '\n')
        {
            if (await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false) == 0)
                throw new IOException("The Docker daemon closed the connection before it answered.");

            buffer.Add(single[0]);
            if (buffer.Count > 64 * 1024)
                throw new IOException("The Docker daemon answered with headers that are too large.");
        }

        var lines = Encoding.ASCII.GetString([.. buffer]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var statusParts = lines[0].Split(' ', 3);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var statusCode))
            throw new IOException("The Docker daemon answered with an invalid status line: " + lines[0]);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.AsSpan(1))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return (statusCode, headers);
    }

    private static async Task<string> ReadBodyAsync(Stream stream, Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length <= 0)
            return "";

        var body = new byte[Math.Min(length, 64 * 1024)];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(body);
    }

    internal static bool TryParseDockerHost(string dockerHost, TlsSettings? tls, out Endpoint endpoint)
    {
        if (dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
        {
            var path = dockerHost["unix://".Length..];
            if (!string.IsNullOrWhiteSpace(path))
            {
                endpoint = Endpoint.ForUnixSocket(FullPath.FromPath(Uri.UnescapeDataString(path)));
                return true;
            }
        }
        else if (dockerHost.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase) &&
                 Uri.TryCreate(dockerHost, UriKind.Absolute, out var npipeUri))
        {
            var server = string.IsNullOrEmpty(npipeUri.Host) ? "." : npipeUri.Host;
            var pipePath = npipeUri.AbsolutePath.Trim('/');
            if (pipePath.StartsWith("pipe/", StringComparison.OrdinalIgnoreCase))
                pipePath = pipePath["pipe/".Length..];

            if (!string.IsNullOrEmpty(pipePath))
            {
                endpoint = Endpoint.ForNamedPipe(server, pipePath.Replace('/', '\\'));
                return true;
            }
        }
        else if (dockerHost.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            // A daemon reached over TCP speaks TLS when the CLI is configured for it, plain HTTP otherwise.
            var scheme = tls is null ? "http://" : "https://";
            if (Uri.TryCreate(scheme + dockerHost["tcp://".Length..], UriKind.Absolute, out var tcpUri))
            {
                endpoint = Endpoint.ForHttp(tcpUri, tls);
                return true;
            }
        }
        else if (Uri.TryCreate(dockerHost, UriKind.Absolute, out var uri) &&
                 (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            endpoint = Endpoint.ForHttp(uri, uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? tls : null);
            return true;
        }

        endpoint = null!;
        return false;
    }

    private static bool TryGetLinuxRootlessSocket(out FullPath path)
    {
        var xdgRuntimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntimeDirectory))
        {
            path = FullPath.FromPath(xdgRuntimeDirectory) / "docker.sock";
            return true;
        }

        var uid = Environment.GetEnvironmentVariable("UID");
        if (!string.IsNullOrEmpty(uid))
        {
            path = FullPath.FromPath(string.Create(CultureInfo.InvariantCulture, $"/run/user/{uid}/docker.sock"));
            return true;
        }

        path = default;
        return false;
    }

    /// <summary>A connection the daemon took over, whose write side can be closed on its own.</summary>
    internal sealed class UpgradedConnection : IAsyncDisposable
    {
        private readonly Socket _socket;
        private readonly SslStream? _sslStream;

        public UpgradedConnection(Socket socket, Stream stream, SslStream? sslStream)
        {
            _socket = socket;
            _sslStream = sslStream;
            Stream = stream;
        }

        public Stream Stream { get; }

        /// <summary>Tells the daemon that the input is over, while the output keeps coming.</summary>
        public async Task CloseWriteAsync()
        {
            if (_sslStream is not null)
                await _sslStream.ShutdownAsync().ConfigureAwait(false);

            _socket.Shutdown(SocketShutdown.Send);
        }

        public async ValueTask DisposeAsync()
        {
            if (_sslStream is not null)
                await _sslStream.DisposeAsync().ConfigureAwait(false);

            await Stream.DisposeAsync().ConfigureAwait(false);
            _socket.Dispose();
        }
    }
}
