namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A container port that is exposed without being published to the host. It is metadata: the other containers of a network reach every port of the container, exposed or not.</summary>
/// <param name="Port">The port exposed inside the container.</param>
/// <param name="Protocol">The transport protocol of the port.</param>
public sealed record ContainerExposedPort(int Port, ContainerPortProtocol Protocol = ContainerPortProtocol.Tcp)
{
    /// <summary>Gets the port exposed inside the container.</summary>
    public int Port { get; init => field = ValidatePort(value); } = ValidatePort(Port);

    private static int ValidatePort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1, nameof(port));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535, nameof(port));
        return port;
    }
}
