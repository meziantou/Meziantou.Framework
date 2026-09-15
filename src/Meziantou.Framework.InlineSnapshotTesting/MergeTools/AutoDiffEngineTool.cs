using Meziantou.Framework.DiffEngine;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class AutoDiffEngineTool : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        var extension = FullPath.FromPath(currentFilePath).Extension;
        if (!DiffTools.TryFindByExtension(extension, out var resolvedTool))
            return null;

        return DiffEngineTool.Start(resolvedTool, currentFilePath, newFilePath, waitForMerge);
    }
}
