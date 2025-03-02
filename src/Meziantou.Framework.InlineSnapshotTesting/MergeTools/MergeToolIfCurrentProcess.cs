using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class MergeToolIfCurrentProcess(MergeTool tool, string[] processNames) : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        var processInfo = ProcessInfo.GetContextProcess();
        if (processInfo is null)
            return null;

        if (processNames.Contains(processInfo.ProcessName, StringComparer.OrdinalIgnoreCase))
            return tool.Start(currentFilePath, newFilePath);

        return null;
    }
}
