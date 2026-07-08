namespace Meziantou.Framework.TemporaryContainers;

/// <summary>The result of a command executed inside a container.</summary>
/// <param name="ExitCode">The command exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
public sealed record ExecResult(int ExitCode, string StandardOutput, string StandardError);
