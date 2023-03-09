using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.InlineSnapshotTesting.Utils;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct ProcessEntry(int ProcessId, int ParentProcessId)
{
    public Process ToProcess()
    {
        return Process.GetProcessById(ProcessId);
    }
}
