namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <summary>Waits until a command run inside the container succeeds.</summary>
internal sealed class CommandWaitStrategy(IReadOnlyList<string> command) : IWaitStrategy
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

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

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    public override string ToString() => "command '" + string.Join(' ', command) + "'";
}
