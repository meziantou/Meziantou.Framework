namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents the status of the health check of a container (the <c>HEALTHCHECK</c> instruction of its image).</summary>
public enum ContainerHealthStatus
{
    /// <summary>The container has no health check, or the runtime does not report it.</summary>
    None,

    /// <summary>The health check has not succeeded yet.</summary>
    Starting,

    /// <summary>The health check succeeds.</summary>
    Healthy,

    /// <summary>The health check failed as many times in a row as it allows.</summary>
    Unhealthy,
}
