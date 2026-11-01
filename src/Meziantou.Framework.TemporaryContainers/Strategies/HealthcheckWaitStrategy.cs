using System.Text;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <summary>Waits until the runtime reports the health check of the container (the <c>HEALTHCHECK</c> of its image) as healthy.</summary>
internal sealed class HealthcheckWaitStrategy(TimeSpan pollInterval) : IWaitStrategy
{
    public Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        return WaitCoreAsync(container.InspectAsync, cancellationToken);
    }

    /// <summary>The wait itself, taking the container state as a delegate so it can be faked.</summary>
    internal async Task WaitCoreAsync(Func<CancellationToken, Task<ContainerInfo>> inspect, CancellationToken cancellationToken)
    {
        while (true)
        {
            var info = await inspect(cancellationToken).ConfigureAwait(false);
            if (info.State is ContainerState.Exited or ContainerState.Removed)
                throw new InvalidOperationException(BuildFailureMessage("The container stopped before its health check succeeded.", info));

            switch (info.Health)
            {
                case ContainerHealthStatus.Healthy:
                    return;

                case ContainerHealthStatus.None:
                    // Waiting would only end with the startup timeout: say why at once.
                    throw new InvalidOperationException(BuildFailureMessage("The container does not report a health status: its image defines no HEALTHCHECK, or the container runtime does not support health checks.", info));

                case ContainerHealthStatus.Unhealthy:
                    throw new InvalidOperationException(BuildFailureMessage("The health check of the container failed.", info));
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildFailureMessage(string reason, ContainerInfo info)
    {
        var message = new StringBuilder(reason);
        message.Append(CultureInfo.InvariantCulture, $" The container is {info.State}");
        if (info.ExitCode is { } exitCode && info.State is ContainerState.Exited)
        {
            message.Append(CultureInfo.InvariantCulture, $" with exit code {exitCode}");
        }

        message.Append('.');
        if (!string.IsNullOrWhiteSpace(info.HealthCheckOutput))
        {
            message.Append(CultureInfo.InvariantCulture, $" Last health check output: {info.HealthCheckOutput.Trim()}");
        }

        return message.ToString();
    }

    public override string ToString() => "healthcheck";
}
