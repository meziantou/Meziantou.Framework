namespace Meziantou.Framework;

/// <summary>Thrown when configured process validation fails.</summary>
public sealed class ProcessExecutionException : Exception
{
    /// <summary>Initializes a new instance of <see cref="ProcessExecutionException"/>.</summary>
    public ProcessExecutionException()
        : base("Process validation failed.")
    {
    }

    /// <summary>Initializes a new instance of <see cref="ProcessExecutionException"/> with the specified message.</summary>
    public ProcessExecutionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of <see cref="ProcessExecutionException"/> with the specified message and inner exception.</summary>
    public ProcessExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of <see cref="ProcessExecutionException"/> with the specified exit code.</summary>
    public ProcessExecutionException(ProcessExitCode exitCode)
        : base($"Process exited with code {exitCode}.")
    {
        ExitCode = exitCode;
    }

    /// <summary>Gets the exit code of the process.</summary>
    public ProcessExitCode ExitCode { get; }
}
