using System.Diagnostics;
using DiffEngine;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class VisualStudioMergeTool : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        if (!DiffTools.TryFindByName(DiffTool.VisualStudio, out var resolvedTool))
            return null;

        var rootFolder = Path.GetDirectoryName(resolvedTool.ExePath) ?? "";
        var vsdiffmerge = Path.Combine(rootFolder, "CommonExtensions", "Microsoft", "TeamFoundation", "Team Explorer", "vsdiffmerge.exe");
        if (!File.Exists(vsdiffmerge))
            return null;

        var originalClone = CopyFileToTemp(currentFilePath);
        var process = Process.Start(vsdiffmerge, $"""
            "{newFilePath}" "{originalClone}" "{originalClone}" "{currentFilePath}" /m
            """);

        if (process is null)
            return null;

        return new ProcessMergeToolResult(process);
    }
}
