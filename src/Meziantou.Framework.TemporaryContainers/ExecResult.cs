namespace Meziantou.Framework.TemporaryContainers;

/// <summary>The result of a command executed inside a container.</summary>
/// <param name="ExitCode">The command exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
public sealed record ExecResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>The command that ran, for the message of <see cref="EnsureSuccess"/>.</summary>
    internal IReadOnlyList<string>? Command { get; init; }

    /// <summary>Throws when the command exited with a non-zero exit code.</summary>
    /// <returns>This instance, so the call can be chained.</returns>
    /// <exception cref="ContainerExecException">The exit code is not 0. The message includes the standard error.</exception>
    public ExecResult EnsureSuccess()
    {
        if (ExitCode != 0)
            throw new ContainerExecException(this, Command ?? []);

        return this;
    }
}
