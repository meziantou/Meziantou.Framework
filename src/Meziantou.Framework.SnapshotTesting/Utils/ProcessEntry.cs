using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.SnapshotTesting.Utils;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct ProcessEntry(int ProcessId, int ParentProcessId)
{
    public Process ToProcess()
    {
        return Process.GetProcessById(ProcessId);
    }
}
