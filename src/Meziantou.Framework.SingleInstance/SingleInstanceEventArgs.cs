namespace Meziantou.Framework;

/// <summary>
/// Provides data for the <see cref="SingleInstance.NewInstance"/> event.
/// </summary>
public sealed class SingleInstanceEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceEventArgs"/> class.
    /// </summary>
    /// <param name="processId">The process ID of the new instance.</param>
    /// <param name="arguments">The command-line arguments of the new instance.</param>
    public SingleInstanceEventArgs(int processId, string[] arguments)
    {
        ProcessId = processId;
        Arguments = arguments;
    }

    /// <summary>
    /// Gets the process ID of the new instance attempting to start.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// Gets the command-line arguments passed to the new instance.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Cannot change the signature, breaking change")]
    public string[] Arguments { get; }
}
