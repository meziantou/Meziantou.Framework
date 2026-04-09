namespace Meziantou.Framework;

/// <summary>Represents a completed process execution.</summary>
public class ProcessResult
{
    internal ProcessResult(int processId, int exitCode, DateTimeOffset startDate, DateTimeOffset exitDate)
    {
        ProcessId = processId;
        ExitCode = exitCode;
        StartDate = startDate;
        ExitDate = exitDate;
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; }

    /// <summary>Gets the time the process was started.</summary>
    public DateTimeOffset StartDate { get; }

    /// <summary>Gets the time the process exited.</summary>
    public DateTimeOffset ExitDate { get; }
}
