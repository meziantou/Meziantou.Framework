namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents the runtime state of a container as reported by the runtime.</summary>
public enum ContainerState
{
    /// <summary>The state could not be determined.</summary>
    Unknown,

    /// <summary>The container has been created but not started.</summary>
    Created,

    /// <summary>The container is running.</summary>
    Running,

    /// <summary>The container is paused.</summary>
    Paused,

    /// <summary>The container has stopped.</summary>
    Exited,

    /// <summary>The container has been removed.</summary>
    Removed,
}
