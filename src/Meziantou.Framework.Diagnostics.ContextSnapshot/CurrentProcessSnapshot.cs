using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class CurrentProcessSnapshot : ProcessSnapshot
{
    internal CurrentProcessSnapshot()
        : base(Process.GetCurrentProcess())
    {
    }

    public string CommandLine { get; } = Environment.CommandLine;
    public string ProcessPath { get; } = Environment.ProcessPath;
    public Architecture ProcessArchitecture { get; } = RuntimeInformation.ProcessArchitecture;
    public bool IsPrivilegedProcess { get; } = Environment.IsPrivilegedProcess;
}
