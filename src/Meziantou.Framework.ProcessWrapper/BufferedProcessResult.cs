namespace Meziantou.Framework;

/// <summary>Represents a completed buffered process execution.</summary>
public sealed class BufferedProcessResult : ProcessResult
{
    internal BufferedProcessResult(int processId, int exitCode, DateTimeOffset startDate, DateTimeOffset exitDate, ProcessOutputCollection output)
        : base(processId, exitCode, startDate, exitDate)
    {
        Output = output;
    }

    /// <summary>Gets the interleaved output from both standard output and standard error streams.</summary>
    public ProcessOutputCollection Output { get; }
}
