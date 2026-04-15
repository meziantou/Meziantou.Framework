using DiffEngine;

namespace Meziantou.Framework.SnapshotTesting.MergeTools;

internal sealed class AutoDiffEngineTool : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        if (!DiffTools.TryFindByExtension(Path.GetExtension(currentFilePath), out var resolvedTool))
            return null;

        return DiffEngineTool.Start(resolvedTool, currentFilePath, newFilePath);
    }
}
