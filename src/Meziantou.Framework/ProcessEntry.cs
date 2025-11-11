using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Represents a process and its parent process relationship.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ProcessEntry : IEquatable<ProcessEntry>
{
    internal ProcessEntry(int processId, int parentProcessId)
    {
        ProcessId = processId;
        ParentProcessId = parentProcessId;
    }

    /// <summary>Gets the process identifier.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the parent process identifier.</summary>
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

    public override string ToString() => $"Id: {ProcessId}; Parent Id: {ParentProcessId}";

    /// <summary>Gets the <see cref="Process"/> instance for this process entry.</summary>
    public Process ToProcess()
    {
        return Process.GetProcessById(ProcessId);
    }

    public static bool operator ==(ProcessEntry left, ProcessEntry right) => left.Equals(right);
    public static bool operator !=(ProcessEntry left, ProcessEntry right) => !(left == right);
}
