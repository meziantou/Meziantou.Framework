namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An entry added to the <c>/etc/hosts</c> file of a container (<c>--add-host</c>).</summary>
/// <param name="Hostname">The host name.</param>
/// <param name="Address">The IP address the host name resolves to, or <see cref="HostGateway"/>.</param>
public sealed record ContainerExtraHost(string Hostname, string Address)
{
    /// <summary>The special address the daemon replaces with the address of the host, which is how a container calls back to the test process on Linux.</summary>
    public const string HostGateway = "host-gateway";

    /// <summary>The host name Docker Desktop resolves to the host on its own, and that <see cref="ForHostGateway"/> uses by default.</summary>
    public const string DefaultHostGatewayHostname = "host.docker.internal";

    /// <summary>Gets the host name.</summary>
    public string Hostname { get; init => field = Validate(value, nameof(Hostname)); } = Validate(Hostname, nameof(Hostname));

    /// <summary>Gets the IP address the host name resolves to, or <see cref="HostGateway"/>.</summary>
    public string Address { get; init => field = Validate(value, nameof(Address)); } = Validate(Address, nameof(Address));

    /// <summary>Creates an entry that resolves a host name to the host, through <see cref="HostGateway"/>.</summary>
    /// <param name="hostname">The host name. Defaults to <see cref="DefaultHostGatewayHostname"/>.</param>
    /// <returns>The entry.</returns>
    public static ContainerExtraHost ForHostGateway(string hostname = DefaultHostGatewayHostname) => new(hostname, HostGateway);

    /// <summary>Formats the entry the way the runtimes take it: <c>hostname:address</c>.</summary>
    internal string Format() => Hostname + ":" + Address;

    private static string Validate(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }
}
