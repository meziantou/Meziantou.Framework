namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <summary>Waits until a command run inside the container succeeds.</summary>
internal sealed class CommandWaitStrategy(IReadOnlyList<string> command, TimeSpan pollInterval) : IWaitStrategy
{
    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        while (true)
        {
            var result = await container.ExecAsync(options =>
            {
                foreach (var token in command)
                    options.Command.Add(token);
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == 0)
                return;

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public override string ToString() => "command '" + string.Join(' ', command) + "'";
}
