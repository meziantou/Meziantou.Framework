using System.Diagnostics;
using Meziantou.Framework.DiffEngine;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class VisualStudioMergeTool : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        if (!DiffTools.TryFindByName(DiffTool.VisualStudio, out var resolvedTool))
            return null;

        var rootFolder = FullPath.FromPath(resolvedTool.ExePath).Parent;
        var vsdiffmerge = rootFolder / "CommonExtensions" / "Microsoft" / "TeamFoundation" / "Team Explorer" / "vsdiffmerge.exe";
        if (!File.Exists(vsdiffmerge))
            return null;

        var originalCopy = CopyFileToTemp(currentFilePath);
        try
        {
            var startInfo = new ProcessStartInfo(vsdiffmerge)
            {
                UseShellExecute = false,
            };

            // vsdiffmerge <source> <target> <base> <result> /m
            startInfo.ArgumentList.Add(newFilePath);
            startInfo.ArgumentList.Add(originalCopy);
            startInfo.ArgumentList.Add(originalCopy);
            startInfo.ArgumentList.Add(currentFilePath);
            startInfo.ArgumentList.Add("/m");

            return ProcessMergeToolResult.Start(startInfo, onExited: () => DeleteTemporaryCopy(originalCopy));
        }
        catch
        {
            DeleteTemporaryCopy(originalCopy);
            throw;
        }
    }

    public override string ToString() => nameof(MergeTool.VisualStudioMerge);
}
