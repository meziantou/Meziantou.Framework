namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents a container port that is published to the host.</summary>
/// <param name="HostPort">The host port to bind, or <see langword="null"/> to let the runtime assign a random host port.</param>
/// <param name="Port">The port exposed inside the container.</param>
public sealed record ContainerPort(int? HostPort, int Port)
{
    /// <summary>Initializes a new instance of the <see cref="ContainerPort"/> class that binds to a random host port.</summary>
    /// <param name="containerPort">The port exposed inside the container.</param>
    public ContainerPort(int containerPort)
        : this(null, containerPort)
    {
    }
}
