namespace Meziantou.Framework.NodeJs;

/// <summary>Options used to start a <see cref="NodeJsHost"/>.</summary>
public sealed class NodeJsHostOptions
{
    /// <summary>Gets the options used when none are provided. It must not be modified.</summary>
    internal static NodeJsHostOptions Default { get; } = new();

    /// <summary>Gets or sets the path of the <c>node</c> executable. When <see langword="null"/>, <c>node</c> is searched in the <c>PATH</c>.</summary>
    public string? NodeExecutablePath { get; set; }

    /// <summary>Gets or sets the working directory of the Node.js process. Module specifiers (relative paths and npm packages) are resolved from this directory. When <see langword="null"/>, the current directory is used.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Gets the additional arguments passed to <c>node</c>, such as <c>--max-old-space-size=4096</c>.</summary>
    public IList<string> NodeArguments { get; } = [];

    /// <summary>Gets the environment variables set for the Node.js process. A <see langword="null"/> value removes the variable.</summary>
    public IDictionary<string, string?> EnvironmentVariables { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Gets or sets the maximum time to wait for the Node.js process to start.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets a callback invoked for each line written by the Node.js process to its standard output. The callback must not throw.</summary>
    public Action<string>? StandardOutputReceived { get; set; }

    /// <summary>Gets or sets a callback invoked for each line written by the Node.js process to its standard error. The callback must not throw.</summary>
    public Action<string>? StandardErrorReceived { get; set; }
}
