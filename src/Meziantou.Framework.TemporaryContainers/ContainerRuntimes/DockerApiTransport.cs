using System.IO.Pipes;
using System.Net.Sockets;
using System.Globalization;
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
        public string DisplayName { get; private set; } = "";

        public static Endpoint ForHttp(Uri baseAddress)
        {
            return new Endpoint(baseAddress)
            {
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

    public static IEnumerable<Endpoint> GetEndpoints()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            if (TryParseDockerHost(dockerHost, out var endpoint))
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

    public static HttpClient CreateClient(Endpoint endpoint)
    {
        SocketsHttpHandler? handler = null;
        try
        {
            handler = new SocketsHttpHandler();
            if (endpoint.UnixSocketPath is { } unixSocketPath)
            {
                handler.ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(unixSocketPath), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                };
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

    private static bool TryParseDockerHost(string dockerHost, out Endpoint endpoint)
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
            if (Uri.TryCreate("http://" + dockerHost["tcp://".Length..], UriKind.Absolute, out var tcpUri))
            {
                endpoint = Endpoint.ForHttp(tcpUri);
                return true;
            }
        }
        else if (Uri.TryCreate(dockerHost, UriKind.Absolute, out var uri) &&
                 (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            endpoint = Endpoint.ForHttp(uri);
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
}
