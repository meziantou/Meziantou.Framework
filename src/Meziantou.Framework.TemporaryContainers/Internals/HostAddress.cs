using System.Net;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Finds the address at which the published ports of a container are reached from this machine.</summary>
internal static class HostAddress
{
    public const string IPv4Loopback = "127.0.0.1";
    public const string IPv6Loopback = "::1";

    /// <summary>Resolves the address of a published port.</summary>
    /// <param name="bindingAddress">The host address the runtime reports for the binding, or <see langword="null"/> when it reports none.</param>
    /// <param name="daemonHost">The host of a daemon that runs on another machine, or <see langword="null"/> for a daemon of this machine.</param>
    public static string Resolve(string? bindingAddress, string? daemonHost)
    {
        if (IPAddress.TryParse(bindingAddress, out var address))
        {
            // An address of the daemon's machine is reached as is, except a loopback address of a remote machine, which
            // only that machine reaches: the daemon host is the best guess left.
            var isAnyAddress = address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
            if (!isAnyAddress && (daemonHost is null || !IPAddress.IsLoopback(address)))
                return address.ToString();

            if (daemonHost is null)
                return address.Equals(IPAddress.IPv6Any) ? IPv6Loopback : IPv4Loopback;
        }

        return daemonHost ?? IPv4Loopback;
    }

    /// <summary>Reads the host of a daemon address such as <c>DOCKER_HOST</c> (<c>tcp://</c>, <c>ssh://</c>, <c>http(s)://</c>, <c>npipe://</c>), or <see langword="null"/> when the daemon runs on this machine.</summary>
    public static string? GetRemoteHost(string? daemonAddress)
    {
        if (string.IsNullOrWhiteSpace(daemonAddress) || !Uri.TryCreate(daemonAddress.Trim(), UriKind.Absolute, out var uri))
            return null;

        return uri.Scheme switch
        {
            "tcp" or "ssh" or "http" or "https" or "npipe" => GetRemoteHostName(uri.Host),
            _ => null,
        };
    }

    /// <summary>Reads the host of the daemon an Engine API endpoint connects to, or <see langword="null"/> when the daemon runs on this machine.</summary>
    public static string? GetRemoteHost(DockerApiTransport.Endpoint endpoint)
    {
        if (endpoint.UnixSocketPath is not null)
            return null;

        if (endpoint.NamedPipeName is not null)
            return GetRemoteHostName(endpoint.NamedPipeServer);

        return GetRemoteHostName(endpoint.BaseAddress.Host);
    }

    /// <summary>Formats a host for the authority of a URI, which encloses an IPv6 address in brackets.</summary>
    public static string FormatForUri(string host) => host.Contains(':', StringComparison.Ordinal) ? "[" + host + "]" : host;

    private static string? GetRemoteHostName(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return null;

        host = host.TrimStart('[').TrimEnd(']');
        if (host is "." || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return null;

        if (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))
            return null;

        return host;
    }
}
