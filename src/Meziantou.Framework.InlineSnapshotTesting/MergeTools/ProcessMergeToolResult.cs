using System.Diagnostics;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class ProcessMergeToolResult(Process process) : MergeToolResult
{
    public Process Process { get; } = process;

    public override void WaitForExit() => Process.WaitForExit();
}
