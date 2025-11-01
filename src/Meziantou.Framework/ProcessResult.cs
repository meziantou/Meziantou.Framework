namespace Meziantou.Framework;

/// <summary>
/// Represents the result of a process execution.
/// </summary>
public sealed class ProcessResult
{
    internal ProcessResult(int exitCode, IReadOnlyList<ProcessOutput> output)
    {
        ExitCode = exitCode;
        Output = new ProcessOutputCollection(output);
    }

    /// <summary>
    /// Gets the exit code of the process.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets the output (standard output and standard error) of the process.
    /// </summary>
    public ProcessOutputCollection Output { get; }
}
