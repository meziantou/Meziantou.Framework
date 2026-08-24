namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Thrown when a container runtime command line exits with a non-zero exit code.</summary>
public sealed class ContainerRuntimeException : Exception
{
    /// <summary>Initializes a new instance of <see cref="ContainerRuntimeException"/>.</summary>
    public ContainerRuntimeException()
        : base("The container runtime command failed.")
    {
    }

    /// <summary>Initializes a new instance of <see cref="ContainerRuntimeException"/> with the specified message.</summary>
    public ContainerRuntimeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of <see cref="ContainerRuntimeException"/> with the specified message and inner exception.</summary>
    public ContainerRuntimeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal ContainerRuntimeException(string message, ContainerRuntime runtime, string command, int exitCode, string standardOutput, string standardError)
        : base(message)
    {
        Runtime = runtime;
        Command = command;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>Gets the runtime whose command line failed.</summary>
    public ContainerRuntime? Runtime { get; }

    /// <summary>Gets the command line that failed. Environment variable values are redacted.</summary>
    public string? Command { get; }

    /// <summary>Gets the exit code of the command.</summary>
    public int ExitCode { get; }

    /// <summary>Gets the text the command wrote to the standard output.</summary>
    public string? StandardOutput { get; }

    /// <summary>Gets the text the command wrote to the standard error.</summary>
    public string? StandardError { get; }
}
