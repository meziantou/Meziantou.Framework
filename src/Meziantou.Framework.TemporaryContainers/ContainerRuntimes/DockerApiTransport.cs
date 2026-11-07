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
    /// <summary>A daemon to connect to. The certificates of <see cref="Tls"/> are loaded by <see cref="TlsCredentials.Load"/>, and belong to the connection.</summary>
    internal sealed class Endpoint
    {
        private Endpoint(Uri baseAddress, Uri address, string source)
        {
            BaseAddress = baseAddress;
            Address = address;
            Source = source;
        }

        /// <summary>The base address of the HTTP requests. It is <c>http://localhost</c> for a socket or a named pipe.</summary>
        public Uri BaseAddress { get; }

        /// <summary>The address of the daemon, as <c>DOCKER_HOST</c> would write it: <c>unix:///var/run/docker.sock</c>, <c>npipe://./pipe/docker_engine</c>, <c>http://host:2375/</c> or <c>https://host:2376/</c>.</summary>
        public Uri Address { get; }

        /// <summary>The name or the address of the machine running the daemon when it is reached over TCP, which is where the published ports of the containers listen; <see langword="null"/> for a socket or a named pipe, whose daemon runs on this machine (or in a virtual machine that forwards the ports to it).</summary>
        public string? TcpHost => UnixSocketPath is null && NamedPipeName is null ? BaseAddress.Host : null;

        /// <summary>Where the endpoint comes from: <c>DOCKER_HOST</c>, a docker context, or the name of a well-known socket.</summary>
        public string Source { get; }

        public FullPath? UnixSocketPath { get; private set; }
        public string? NamedPipeServer { get; private set; }
        public string? NamedPipeName { get; private set; }
        public TlsSettings? Tls { get; private set; }
        public string DisplayName { get; private set; } = "";

        public static Endpoint ForHttp(Uri baseAddress, TlsSettings? tls = null, string source = "DOCKER_HOST")
        {
            return new Endpoint(baseAddress, baseAddress, source)
            {
                Tls = tls,
                DisplayName = baseAddress.ToString(),
            };
        }

        public static Endpoint ForUnixSocket(FullPath socketPath, string source = "DOCKER_HOST")
        {
            var displayName = "unix://" + socketPath.Value;
            return new Endpoint(new Uri("http://localhost"), new Uri("unix:///" + socketPath.Value.Replace('\\', '/').TrimStart('/')), source)
            {
                UnixSocketPath = socketPath,
                DisplayName = displayName,
            };
        }

        public static Endpoint ForNamedPipe(string server, string name, string source = "DOCKER_HOST")
        {
            return new Endpoint(new Uri("http://localhost"), new Uri("npipe://" + server + "/pipe/" + name.Replace('\\', '/')), source)
            {
                NamedPipeServer = server,
                NamedPipeName = name,
                DisplayName = @"npipe://\\" + server + @"\pipe\" + name,
            };
        }

        public override string ToString() => DisplayName + " (" + Source + ")";
    }

    /// <summary>The TLS configuration of a daemon reached over TCP.</summary>
    /// <param name="CertificateDirectory">The directory holding <c>ca.pem</c>, <c>cert.pem</c> and <c>key.pem</c>.</param>
    /// <param name="VerifyServerCertificate">Whether the daemon certificate is verified. The docker CLI does not verify it with <c>DOCKER_TLS</c> alone, nor for a context created with <c>skip-tls-verify</c>.</param>
    internal sealed record TlsSettings(FullPath CertificateDirectory, bool VerifyServerCertificate = true);

    /// <summary>The client certificate and the certificate authorities of a <see cref="TlsSettings"/>, loaded once and shared by every connection to the daemon.</summary>
    internal sealed class TlsCredentials : IDisposable
    {
        private TlsCredentials(bool verifyServerCertificate, X509Certificate2? clientCertificate, X509Certificate2Collection authorities)
        {
            VerifyServerCertificate = verifyServerCertificate;
            ClientCertificate = clientCertificate;
            Authorities = authorities;
        }

        public bool VerifyServerCertificate { get; }

        public X509Certificate2? ClientCertificate { get; }

        /// <summary>Every certificate of <c>ca.pem</c>: a bundle can hold an intermediate authority along with the root.</summary>
        public X509Certificate2Collection Authorities { get; }

        public static TlsCredentials Load(TlsSettings settings)
        {
            X509Certificate2? clientCertificate = null;
            var authorities = new X509Certificate2Collection();
            try
            {
                var certificatePath = settings.CertificateDirectory / "cert.pem";
                var keyPath = settings.CertificateDirectory / "key.pem";
                if (File.Exists(certificatePath) && File.Exists(keyPath))
                {
                    using var pemCertificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

                    // SChannel cannot use the ephemeral key of a certificate loaded from PEM files, so it goes through PKCS#12.
                    clientCertificate = X509CertificateLoader.LoadPkcs12(pemCertificate.Export(X509ContentType.Pkcs12), password: null);
                }

                // The daemon certificate is signed by the certificate authority of the directory, which the machine does
                // not trust on its own. The authorities only matter when the certificate is verified.
                var authorityPath = settings.CertificateDirectory / "ca.pem";
                if (settings.VerifyServerCertificate && File.Exists(authorityPath))
                    authorities.ImportFromPemFile(authorityPath);

                var credentials = new TlsCredentials(settings.VerifyServerCertificate, clientCertificate, authorities);
                clientCertificate = null;
                authorities = null;
                return credentials;
            }
            finally
            {
                clientCertificate?.Dispose();
                DisposeAll(authorities);
            }
        }

        public SslClientAuthenticationOptions CreateSslOptions(string host)
        {
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
            };

            if (ClientCertificate is not null)
                options.ClientCertificates = [ClientCertificate];

            if (!VerifyServerCertificate || Authorities.Count > 0)
                options.RemoteCertificateValidationCallback = (_, certificate, chain, errors) => IsTrusted(certificate, chain, errors);

            return options;
        }

        internal bool IsTrusted(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (!VerifyServerCertificate || errors is SslPolicyErrors.None)
                return true;

            // Only the chain is re-evaluated: a certificate whose name does not match the host is still rejected.
            if (certificate is null || Authorities.Count == 0 || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
                return false;

            using var customChain = new X509Chain();
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            customChain.ChainPolicy.CustomTrustStore.AddRange(Authorities);
            customChain.ChainPolicy.ExtraStore.AddRange(Authorities);
            if (chain is not null)
                customChain.ChainPolicy.ExtraStore.AddRange(chain.ChainPolicy.ExtraStore);

            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            using var serverCertificate = new X509Certificate2(certificate);
            return customChain.Build(serverCertificate);
        }

        public void Dispose()
        {
            ClientCertificate?.Dispose();
            DisposeAll(Authorities);
        }

        private static void DisposeAll(X509Certificate2Collection? certificates)
        {
            if (certificates is null)
                return;

            foreach (var certificate in certificates)
                certificate.Dispose();
        }
    }

    public static HttpClient CreateClient(Endpoint endpoint, TlsCredentials? tls)
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
            else if (tls is not null)
            {
                handler.SslOptions = tls.CreateSslOptions(endpoint.BaseAddress.Host);
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

    /// <summary>Sends a request whose connection the daemon takes over, and returns that connection once the response headers are read. The standard input of an exec needs it: the process only sees the end of its input when the client closes its side of the connection, which an <see cref="HttpClient"/> cannot do.</summary>
    /// <returns>The connection, or the status and the body the daemon refused the request with.</returns>
    /// <exception cref="NotSupportedException">The daemon is reached over a named pipe, which cannot be half-closed.</exception>
    public static async Task<(UpgradedConnection? Connection, HttpStatusCode StatusCode, string ErrorBody)> SendUpgradeRequestAsync(Endpoint endpoint, TlsCredentials? tls, string pathAndQuery, string jsonBody, CancellationToken cancellationToken)
    {
        if (endpoint.NamedPipeName is not null)
            throw new NotSupportedException("The Docker API runtime cannot send a standard input to a command over a named pipe. Use the 'docker' CLI runtime instead.");

        var connection = await ConnectRawAsync(endpoint, tls, cancellationToken).ConfigureAwait(false);
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

    private static async Task<UpgradedConnection> ConnectRawAsync(Endpoint endpoint, TlsCredentials? tls, CancellationToken cancellationToken)
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
            if (tls is null)
            {
                var plain = new UpgradedConnection(socket, networkStream, sslStream: null);
                socket = null;
                return plain;
            }

            var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
            try
            {
                await sslStream.AuthenticateAsClientAsync(tls.CreateSslOptions(endpoint.BaseAddress.Host), cancellationToken).ConfigureAwait(false);
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

    internal static bool TryParseDockerHost(string dockerHost, TlsSettings? tls, [NotNullWhen(true)] out Endpoint? endpoint, string source = "DOCKER_HOST")
    {
        if (dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
        {
            var path = dockerHost["unix://".Length..];
            if (!string.IsNullOrWhiteSpace(path))
            {
                endpoint = Endpoint.ForUnixSocket(FullPath.FromPath(Uri.UnescapeDataString(path)), source);
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
                endpoint = Endpoint.ForNamedPipe(server, pipePath.Replace('/', '\\'), source);
                return true;
            }
        }
        else if (dockerHost.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            // A daemon reached over TCP speaks TLS when the CLI is configured for it, plain HTTP otherwise.
            var scheme = tls is null ? "http://" : "https://";
            if (Uri.TryCreate(scheme + dockerHost["tcp://".Length..], UriKind.Absolute, out var tcpUri))
            {
                endpoint = Endpoint.ForHttp(tcpUri, tls, source);
                return true;
            }
        }
        else if (Uri.TryCreate(dockerHost, UriKind.Absolute, out var uri) &&
                 (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            endpoint = Endpoint.ForHttp(uri, uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? tls : null, source);
            return true;
        }

        endpoint = null;
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
