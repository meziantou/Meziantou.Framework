using System.Net;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Thrown when the container runtime fails an operation: its command line exits with a non-zero exit code, or the Docker Engine API refuses a request or reports a failure. Every runtime reports its failures with this exception.</summary>
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

    internal ContainerRuntimeException(string message, ContainerRuntime runtime, HttpStatusCode? statusCode)
        : base(message)
    {
        Runtime = runtime;
        StatusCode = statusCode;
    }

    /// <summary>Gets the runtime whose operation failed.</summary>
    public ContainerRuntime? Runtime { get; }

    /// <summary>Gets the command line that failed, or <see langword="null"/> when the operation did not run a command line. Environment variable values are redacted.</summary>
    public string? Command { get; }

    /// <summary>Gets the exit code of the command, or 0 when the operation did not run a command line.</summary>
    public int ExitCode { get; }

    /// <summary>Gets the text the command wrote to the standard output.</summary>
    public string? StandardOutput { get; }

    /// <summary>Gets the text the command wrote to the standard error.</summary>
    public string? StandardError { get; }

    /// <summary>Gets the status of the Docker Engine API response that refused the request, or <see langword="null"/> when the failure was not reported by a response status: a command line that failed, or an operation the daemon reported as failed in the body of a successful response.</summary>
    public HttpStatusCode? StatusCode { get; }
}
