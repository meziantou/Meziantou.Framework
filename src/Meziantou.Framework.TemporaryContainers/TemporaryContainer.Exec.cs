namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Executes a command inside the running container.</summary>
    /// <param name="configure">A callback used to configure the command and its options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The exit code and captured output of the command. Call <see cref="ExecResult.EnsureSuccess"/> to throw when the command failed.</returns>
    /// <exception cref="TimeoutException">The command did not exit within <see cref="ExecOptions.Timeout"/>.</exception>
    public async Task<ExecResult> ExecAsync(Action<ExecOptions> configure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var id = RequireId();

        var options = new ExecOptions();
        configure(options);

        if (options.Command is null)
            throw new InvalidOperationException("ExecOptions.Command must be set.");

        if (options.Command.Count == 0)
            throw new InvalidOperationException("ExecOptions.Command must contain at least one element.");

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero && timeout != System.Threading.Timeout.InfiniteTimeSpan)
            throw new InvalidOperationException("ExecOptions.Timeout must be greater than zero.");

        if (options.Detach && (options.StandardInput is not null || options.StandardOutput is not null || options.StandardError is not null || options.Timeout is not null))
            throw new InvalidOperationException("A detached command cannot have a standard input, an output stream or a timeout: it keeps running after ExecAsync returns.");

        var command = options.Command.ToArray();
        if (options.Timeout is not { } execTimeout || execTimeout == System.Threading.Timeout.InfiniteTimeSpan)
            return await Runtime.ExecAsync(id, options, cancellationToken).ConfigureAwait(false) with { Command = command };

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(execTimeout);
        try
        {
            return await Runtime.ExecAsync(id, options, timeoutSource.Token).ConfigureAwait(false) with { Command = command };
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"The command '{string.Join(' ', command)}' did not exit within {execTimeout}."), ex);
        }
    }
}
