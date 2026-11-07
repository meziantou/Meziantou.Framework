using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the current process including command line, process path, architecture, and privilege status.</summary>
public sealed class CurrentProcessSnapshot : ProcessSnapshot
{
    internal CurrentProcessSnapshot()
        : this(Process.GetCurrentProcess())
    {
    }

    private CurrentProcessSnapshot(Process process)
        : base(process)
    {
        // The Process is owned by this constructor, so it is disposed once every property has been read.
        process.Dispose();
    }

    public string CommandLine { get; } = Environment.CommandLine;
    public string? ProcessPath { get; } = Environment.ProcessPath;
    public Architecture ProcessArchitecture { get; } = RuntimeInformation.ProcessArchitecture;
    public bool IsPrivilegedProcess { get; } = Environment.IsPrivilegedProcess;
}
