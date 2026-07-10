namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A snapshot of a container's state, obtained from an inspect operation.</summary>
public sealed record ContainerInfo
{
    /// <summary>Gets the container id.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the container name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the image the container was created from.</summary>
    public string? Image { get; init; }

    /// <summary>Gets the container state.</summary>
    public ContainerState State { get; init; }

    /// <summary>Gets the raw status string reported by the runtime.</summary>
    public string? Status { get; init; }

    /// <summary>Gets the time the container was last started, when available.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets the time the container last exited, when available.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Gets the exit code of the container, when it has exited.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Gets the container IP address on its primary network, when available.</summary>
    public string? IPAddress { get; init; }

    /// <summary>Gets the published ports, mapping each container port to its host port.</summary>
    public IReadOnlyDictionary<int, int> Ports { get; init; } = new Dictionary<int, int>();

    /// <summary>Gets the container labels.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
