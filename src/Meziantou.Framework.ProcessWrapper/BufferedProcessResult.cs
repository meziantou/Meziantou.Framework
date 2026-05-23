namespace Meziantou.Framework;

/// <summary>Represents a completed buffered process execution.</summary>
public sealed class BufferedProcessResult : ProcessResult
{
    internal BufferedProcessResult(int processId, ProcessExitCode exitCode, DateTimeOffset startDate, DateTimeOffset exitDate, ProcessOutputCollection output, string processFileName, IReadOnlyList<string> arguments, ProcessLogVerbosity logVerbosity)
        : base(processId, exitCode, startDate, exitDate, processFileName, arguments, logVerbosity)
    {
        Output = output;
    }

    /// <summary>Gets the interleaved output from both standard output and standard error streams.</summary>
    public ProcessOutputCollection Output { get; }
}
