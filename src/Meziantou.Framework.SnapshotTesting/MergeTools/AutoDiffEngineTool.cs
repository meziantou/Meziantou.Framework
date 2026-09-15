using Meziantou.Framework.DiffEngine;

namespace Meziantou.Framework.SnapshotTesting.MergeTools;

internal sealed class AutoDiffEngineTool : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        if (!DiffTools.TryFindByExtension(Path.GetExtension(currentFilePath), out var resolvedTool))
            return null;

        return DiffEngineTool.Start(resolvedTool, currentFilePath, newFilePath, waitForMerge);
    }
}
