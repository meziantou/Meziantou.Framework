namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Options for a command executed inside a running container.</summary>
public sealed class ExecOptions
{
    /// <summary>Gets the command and its arguments. The first element is the executable.</summary>
    public IList<string> Command { get; } = [];

    /// <summary>Gets or sets the working directory inside the container.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Gets or sets the user to run the command as.</summary>
    public string? User { get; set; }

    /// <summary>Gets the environment variables set for the command.</summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the input piped to the command's standard input.</summary>
    public InputSource? StandardInput { get; set; }
}
