namespace Meziantou.Framework.TemporaryContainers.Tests;

/// <summary>Helpers shared by the container integration tests. Container runtimes fail transiently on CI agents (image pull races, port collisions, daemon hiccups), so the operations that are not under test are retried.</summary>
internal static class ContainerTestHelper
{
    private const int MaxStartAttempts = 4;

    public static Task<TemporaryContainer> StartWithRetryAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        return StartWithRetryAsync(definition.CreateContainer, cancellationToken);
    }

    public static async Task<TContainer> StartWithRetryAsync<TContainer>(Func<TContainer> containerFactory, CancellationToken cancellationToken)
        where TContainer : TemporaryContainer
    {
        var failures = new List<Exception>();
        for (var attempt = 1; ; attempt++)
        {
            var container = containerFactory();
            try
            {
                await container.StartAsync(cancellationToken);
                return container;
            }
            catch (Exception ex)
            {
                // The container may already exist when the failure happens (for instance when a wait strategy times out), so it must be removed before retrying.
                await DisposeSafeAsync(container);
                cancellationToken.ThrowIfCancellationRequested();
                failures.Add(ex);

                if (attempt >= MaxStartAttempts)
                    throw new AggregateException($"The container could not be started after {MaxStartAttempts} attempts.", failures);

                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }

    /// <summary>Disposes a container without ever throwing, so a cleanup failure cannot hide the failure under test.</summary>
    private static async ValueTask DisposeSafeAsync(TemporaryContainer container)
    {
        try
        {
            await container.DisposeAsync();
        }
        catch (Exception ex)
        {
            TestContext.Current.TestOutputHelper?.WriteLine("Failed to dispose the container: " + ex);
        }
    }
}
