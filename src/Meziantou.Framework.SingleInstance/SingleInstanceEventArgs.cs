namespace Meziantou.Framework;

/// <summary>
/// Provides data for the <see cref="SingleInstance.NewInstance"/> event when another instance of the application attempts to start.
/// </summary>
public sealed class SingleInstanceEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceEventArgs"/> class with the specified process ID and arguments.
    /// </summary>
    /// <param name="processId">The process ID of the new instance that is attempting to start.</param>
    /// <param name="arguments">The command-line arguments passed to the new instance.</param>
    public SingleInstanceEventArgs(int processId, string[] arguments)
    {
        ProcessId = processId;
        Arguments = arguments;
    }

    /// <summary>
    /// Gets the process ID of the new instance that is attempting to start.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// Gets the command-line arguments passed to the new instance.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Cannot change the signature, breaking change")]
    public string[] Arguments { get; }
}
