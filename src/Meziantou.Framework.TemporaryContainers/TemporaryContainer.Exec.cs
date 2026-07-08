namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Executes a command inside the running container.</summary>
    /// <param name="configure">A callback used to configure the command and its options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The exit code and captured output of the command.</returns>
    public async Task<ExecResult> ExecAsync(Action<ExecOptions> configure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var id = RequireId();

        var options = new ExecOptions();
        configure(options);

        if (options.Command.Count == 0)
            throw new InvalidOperationException("ExecOptions.Command must contain at least one element.");

        var args = Adapter.BuildExecArguments(id, options);
        var result = await Cli.RunBufferedAsync(args, cancellationToken, allowNonZero: true, input: options.StandardInput).ConfigureAwait(false);
        return new ExecResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }
}
