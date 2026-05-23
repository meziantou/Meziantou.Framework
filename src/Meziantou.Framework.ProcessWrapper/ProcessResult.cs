using System.Text;

namespace Meziantou.Framework;

/// <summary>Represents a completed process execution.</summary>
public class ProcessResult
{
    private readonly string _processFileName;
    private readonly IReadOnlyList<string> _arguments;

    internal ProcessResult(int processId, ProcessExitCode exitCode, DateTimeOffset startDate, DateTimeOffset exitDate, string processFileName, IReadOnlyList<string> arguments)
    {
        ProcessId = processId;
        ExitCode = exitCode;
        StartDate = startDate;
        ExitDate = exitDate;
        _processFileName = processFileName ?? throw new ArgumentNullException(nameof(processFileName));
        _arguments = [.. arguments ?? throw new ArgumentNullException(nameof(arguments))];
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
        var commandLine = CommandLineBuilder.WindowsQuotedArgument(_processFileName);
        if (_arguments.Count == 0)
            return $"{commandLine} (ExitCode: {ExitCode})";

        var sb = new StringBuilder(commandLine);

        foreach (var argument in _arguments)
        {
            sb.Append(' ');
            sb.Append(CommandLineBuilder.WindowsQuotedArgument(argument));
        }

        sb.Append(" (ExitCode: ");
        sb.Append(ExitCode);
        sb.Append(')');
        return sb.ToString();
    }
}
