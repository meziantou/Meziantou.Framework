using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly struct ProcessEntry : IEquatable<ProcessEntry>
{
    internal ProcessEntry(int processId, int parentProcessId)
    {
        ProcessId = processId;
        ParentProcessId = parentProcessId;
    }

    public int ProcessId { get; }
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

    public Process ToProcess()
    {
        return Process.GetProcessById(ProcessId);
    }

    public static bool operator ==(ProcessEntry left, ProcessEntry right) => left.Equals(right);
    public static bool operator !=(ProcessEntry left, ProcessEntry right) => !(left == right);
}
