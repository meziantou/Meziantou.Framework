namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Thrown by <see cref="ExecResult.EnsureSuccess"/> when a command executed inside a container exits with a non-zero exit code.</summary>
public sealed class ContainerExecException : Exception
{
    /// <summary>Initializes a new instance of <see cref="ContainerExecException"/>.</summary>
    public ContainerExecException()
        : base("The command executed in the container failed.")
    {
        StandardOutput = "";
        StandardError = "";
        Command = [];
    }

    /// <summary>Initializes a new instance of <see cref="ContainerExecException"/> with the specified message.</summary>
    /// <param name="message">The message.</param>
    public ContainerExecException(string message)
        : base(message)
    {
        StandardOutput = "";
        StandardError = "";
        Command = [];
    }

    /// <summary>Initializes a new instance of <see cref="ContainerExecException"/> with the specified message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ContainerExecException(string message, Exception innerException)
        : base(message, innerException)
    {
        StandardOutput = "";
        StandardError = "";
        Command = [];
    }

    internal ContainerExecException(ExecResult result, IReadOnlyList<string> command)
        : base(BuildMessage(result, command))
    {
        ExitCode = result.ExitCode;
        StandardOutput = result.StandardOutput;
        StandardError = result.StandardError;
        Command = command;
    }

    /// <summary>Gets the exit code of the command.</summary>
    public int ExitCode { get; }

    /// <summary>Gets the command and its arguments.</summary>
    public IReadOnlyList<string> Command { get; }

    /// <summary>Gets the text the command wrote to the standard output.</summary>
    public string StandardOutput { get; }

    /// <summary>Gets the text the command wrote to the standard error.</summary>
    public string StandardError { get; }

    private static string BuildMessage(ExecResult result, IReadOnlyList<string> command)
    {
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"The command exited with code {result.ExitCode}.");
        if (command.Count > 0)
            message.Append("\nCommand: ").Append(string.Join(' ', command.Select(static argument => argument.Contains(' ', StringComparison.Ordinal) ? "\"" + argument + "\"" : argument)));

        if (!string.IsNullOrWhiteSpace(result.StandardError))
            message.Append("\nStandard error:\n").Append(result.StandardError.TrimEnd());

        // Many tools report their errors on the standard output, which is the only clue left when the standard error is empty.
        else if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            message.Append("\nStandard output:\n").Append(result.StandardOutput.TrimEnd());

        return message.ToString();
    }
}
