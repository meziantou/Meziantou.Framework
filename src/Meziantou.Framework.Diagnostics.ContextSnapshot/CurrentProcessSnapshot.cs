using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the current process at a specific point in time.</summary>
public sealed class CurrentProcessSnapshot : ProcessSnapshot
{
    internal CurrentProcessSnapshot()
        : base(Process.GetCurrentProcess())
    {
    }

    /// <summary>Gets the command line used to start the process.</summary>
    public string CommandLine { get; } = Environment.CommandLine;
    /// <summary>Gets the path to the executable file of the process.</summary>
    public string ProcessPath { get; } = Environment.ProcessPath;
    /// <summary>Gets the processor architecture of the process.</summary>
    public Architecture ProcessArchitecture { get; } = RuntimeInformation.ProcessArchitecture;
    /// <summary>Gets a value indicating whether the process is running with elevated privileges.</summary>
    public bool IsPrivilegedProcess { get; } = Environment.IsPrivilegedProcess;
}
