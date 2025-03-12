using System.Diagnostics;
using DiffEngine;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class DiffEngineTool(DiffTool tool) : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        if (!DiffTools.TryFindByName(tool, out var resolvedTool))
            return null;

        return Start(resolvedTool, currentFilePath, newFilePath);
    }

    internal static MergeToolResult? Start(ResolvedTool resolvedTool, string currentFilePath, string newFilePath)
    {
        var arguments = resolvedTool.GetArguments(newFilePath, currentFilePath);
        var startInfo = new ProcessStartInfo(resolvedTool.ExePath, arguments)
        {
            UseShellExecute = true,
        };

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is not null)
                return new ProcessMergeToolResult(process);

            throw new InlineSnapshotException($"Failed to launch diff tool: {resolvedTool.ExePath} {arguments}");
        }
        catch (Exception exception)
        {
            process?.Dispose();
            throw new InlineSnapshotException($"Failed to launch diff tool: {resolvedTool.ExePath} {arguments}", exception);
        }
    }

    public override string ToString() => tool.ToString();
}
