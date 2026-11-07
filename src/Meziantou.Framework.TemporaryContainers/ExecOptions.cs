namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Options for a command executed inside a running container.</summary>
public sealed class ExecOptions
{
    /// <summary>Gets or sets the command and its arguments. The first element is the executable.</summary>
    public IList<string> Command { get; set; } = [];

    /// <summary>Gets or sets the working directory inside the container.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Gets or sets the user to run the command as.</summary>
    public string? User { get; set; }

    /// <summary>Gets the environment variables set for the command.</summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the input piped to the command's standard input.</summary>
    public InputSource? StandardInput { get; set; }

    /// <summary>Gets or sets the stream the standard output of the command is copied to, byte for byte. When it is set, <see cref="ExecResult.StandardOutput"/> is empty.</summary>
    /// <remarks>Use it for binary output, such as the output of <c>pg_dump</c> or <c>tar</c>, which decoding it as text would corrupt, or for an output too large to keep in memory. The stream is not disposed.</remarks>
    public Stream? StandardOutput { get; set; }

    /// <summary>Gets or sets the stream the standard error of the command is copied to, byte for byte. When it is set, <see cref="ExecResult.StandardError"/> is empty.</summary>
    /// <remarks>The stream is not disposed. When <see cref="Tty"/> is set, the command writes both outputs to the terminal, and everything goes to the standard output.</remarks>
    public Stream? StandardError { get; set; }

    /// <summary>Gets or sets a value indicating whether the command runs in a pseudo-terminal. Some commands only print colors or progress, or only behave interactively, in a terminal.</summary>
    /// <remarks>A terminal merges the standard error into the standard output, and turns line feeds into <c>\r\n</c>.</remarks>
    public bool Tty { get; set; }

    /// <summary>Gets or sets a value indicating whether the command runs with every capability. Apple's container runtime does not support it.</summary>
    public bool Privileged { get; set; }

    /// <summary>Gets or sets the maximum time to wait for the command. When it elapses, <see cref="TemporaryContainer.ExecAsync"/> throws a <see cref="TimeoutException"/>. <see langword="null"/> waits until the command exits or the cancellation token is canceled.</summary>
    /// <remarks>The runtimes cannot stop a command they started without a terminal: the command may keep running in the container after the timeout.</remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Gets or sets a value indicating whether the command is started in the background. <see cref="TemporaryContainer.ExecAsync"/> returns as soon as it is started, with an exit code of 0 and no output, and the command keeps running.</summary>
    /// <remarks>A detached command cannot have a <see cref="StandardInput"/>, a <see cref="StandardOutput"/>, a <see cref="StandardError"/> or a <see cref="Timeout"/>. The <c>wslc</c> runtime does not support it and throws a <see cref="NotSupportedException"/>.</remarks>
    public bool Detach { get; set; }
}
