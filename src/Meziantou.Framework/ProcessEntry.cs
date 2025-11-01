using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Represents a process entry with its ID and parent process ID.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ProcessEntry : IEquatable<ProcessEntry>
{
    internal ProcessEntry(int processId, int parentProcessId)
    {
        ProcessId = processId;
        ParentProcessId = parentProcessId;
    }

    /// <summary>
    /// Gets the process ID.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// Gets the parent process ID.
    /// </summary>
    public int ParentProcessId { get; }

    public override bool Equals(object? obj)
    {
        return obj is ProcessEntry entry && Equals(entry);
    }

    public bool Equals(ProcessEntry other)
    {
        return ProcessId == other.ProcessId && ParentProcessId == other.ParentProcessId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ProcessId, ParentProcessId);
    }

    /// <inheritdoc/>
    public override string ToString() => $"Id: {ProcessId}; Parent Id: {ParentProcessId}";

    /// <summary>
    /// Converts this process entry to a <see cref="Process"/> object.
    /// </summary>
    /// <returns>A <see cref="Process"/> object representing this process entry.</returns>
    /// <exception cref="ArgumentException">Thrown when the process with the specified ID does not exist.</exception>
    public Process ToProcess()
    {
        return Process.GetProcessById(ProcessId);
    }

    public static bool operator ==(ProcessEntry left, ProcessEntry right) => left.Equals(right);
    public static bool operator !=(ProcessEntry left, ProcessEntry right) => !(left == right);
}
