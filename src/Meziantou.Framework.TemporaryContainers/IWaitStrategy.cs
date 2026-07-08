namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents a readiness check that completes once a container is ready to be used.</summary>
public interface IWaitStrategy
{
    /// <summary>Waits until the container satisfies this strategy.</summary>
    /// <param name="container">The container to check.</param>
    /// <param name="cancellationToken">A token used to abort the wait (for example when the startup timeout elapses).</param>
    /// <returns>A task that completes when the container is ready.</returns>
    Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken);
}
