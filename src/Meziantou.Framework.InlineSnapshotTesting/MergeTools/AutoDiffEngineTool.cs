using DiffEngine;
using Meziantou.Framework;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class AutoDiffEngineTool : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        var extension = FullPath.FromPath(currentFilePath).Extension;
        if (!DiffTools.TryFindByExtension(extension, out var resolvedTool))
            return null;

        return DiffEngineTool.Start(resolvedTool, currentFilePath, newFilePath);
    }
}
