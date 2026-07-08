namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
