namespace Meziantou.Framework;

/// <summary>Represents a completed process execution.</summary>
public class ProcessResult
{
    private readonly string _processFileName;
    private readonly IReadOnlyList<string> _arguments;
    private readonly ProcessLogVerbosity _logVerbosity;

    internal ProcessResult(int processId, ProcessExitCode exitCode, DateTimeOffset startDate, DateTimeOffset exitDate, string processFileName, IReadOnlyList<string> arguments, ProcessLogVerbosity logVerbosity)
    {
        ProcessId = processId;
        ExitCode = exitCode;
        StartDate = startDate;
        ExitDate = exitDate;
        _processFileName = processFileName ?? throw new ArgumentNullException(nameof(processFileName));
        _arguments = [.. arguments ?? throw new ArgumentNullException(nameof(arguments))];
        _logVerbosity = logVerbosity;
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the process exit code.</summary>
    public ProcessExitCode ExitCode { get; }

    /// <summary>Gets the time the process was started.</summary>
    public DateTimeOffset StartDate { get; }

    /// <summary>Gets the time the process exited.</summary>
    public DateTimeOffset ExitDate { get; }

    /// <summary>Returns the command line and exit code of the process execution result.</summary>
    public override string ToString()
    {
        var commandLine = ProcessCommandLineFormatter.Format(_processFileName, _arguments, _logVerbosity);
        if (string.IsNullOrEmpty(commandLine))
        {
            return $"(ExitCode: {ExitCode})";
        }

        return $"{commandLine} (ExitCode: {ExitCode})";
    }
}
