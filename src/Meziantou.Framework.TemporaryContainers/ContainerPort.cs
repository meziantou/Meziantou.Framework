using System.Net;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents a container port that is published to the host.</summary>
/// <param name="HostPort">The host port to bind, or <see langword="null"/> to let the runtime assign a random host port.</param>
/// <param name="Port">The port exposed inside the container.</param>
public sealed record ContainerPort(int? HostPort, int Port)
{
    /// <summary>The host address a port is published on unless <see cref="HostIp"/> says otherwise: the loopback address, so the container is only reachable from this machine.</summary>
    public const string DefaultHostIp = "127.0.0.1";

    /// <summary>Initializes a new instance of the <see cref="ContainerPort"/> class that binds to a random host port.</summary>
    /// <param name="containerPort">The port exposed inside the container.</param>
    public ContainerPort(int containerPort)
        : this(null, containerPort)
    {
    }

    /// <summary>Gets the host port to bind, or <see langword="null"/> to let the runtime assign a random host port.</summary>
    public int? HostPort { get; init => field = ValidateHostPort(value); } = ValidateHostPort(HostPort);

    /// <summary>Gets the port exposed inside the container.</summary>
    public int Port { get; init => field = ValidatePort(value); } = ValidatePort(Port);

    /// <summary>Gets the host address the port is published on. Defaults to <see cref="DefaultHostIp"/>, so a test database is not reachable from the network; use <c>0.0.0.0</c> to publish the port on every interface.</summary>
    /// <remarks>Windows containers cannot be published on a specific address, and <c>wslc</c> publishes through the loopback forwarding of WSL, so the address is not passed to them.</remarks>
    public string HostIp { get; init => field = ValidateHostIp(value); } = DefaultHostIp;

    private static int? ValidateHostPort(int? hostPort)
    {
        if (hostPort is { } value)
            ValidateRange(value, nameof(hostPort));

        return hostPort;
    }

    private static int ValidatePort(int port)
    {
        ValidateRange(port, nameof(port));
        return port;
    }

    private static void ValidateRange(int value, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 65535, paramName);
    }

    private static string ValidateHostIp(string hostIp)
    {
        ArgumentNullException.ThrowIfNull(hostIp);
        if (!IPAddress.TryParse(hostIp, out _))
            throw new ArgumentException($"'{hostIp}' is not an IP address.", nameof(hostIp));

        return hostIp;
    }
}
