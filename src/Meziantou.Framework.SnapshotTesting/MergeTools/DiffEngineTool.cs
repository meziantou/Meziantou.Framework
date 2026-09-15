using System.Diagnostics;
using Meziantou.Framework.DiffEngine;

namespace Meziantou.Framework.SnapshotTesting.MergeTools;

internal sealed class DiffEngineTool(DiffTool tool) : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        if (!DiffTools.TryFindByName(tool, out var resolvedTool))
            return null;

        return Start(resolvedTool, currentFilePath, newFilePath, waitForMerge);
    }

    internal static MergeToolResult? Start(ResolvedTool resolvedTool, string currentFilePath, string newFilePath, bool waitForMerge)
    {
        var arguments = resolvedTool.GetArguments(newFilePath, currentFilePath);

        // Most launchers of an IDE hand the files over to the running instance and exit right away. VS Code and Cursor
        // can be asked to wait until the diff is closed; the other tools give no such guarantee.
        var waitsForMerge = false;
        if (waitForMerge && resolvedTool.Tool is DiffTool.VisualStudioCode or DiffTool.Cursor)
        {
            arguments = "--wait " + arguments;
            waitsForMerge = true;
        }

        var startInfo = new ProcessStartInfo(resolvedTool.ExePath, arguments)
        {
            UseShellExecute = true,
        };

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is not null)
                return new ProcessMergeToolResult(process, waitsForMerge);

            throw new SnapshotException($"Failed to launch diff tool: {resolvedTool.ExePath} {arguments}");
        }
        catch (Exception exception)
        {
            process?.Dispose();
            throw new SnapshotException($"Failed to launch diff tool: {resolvedTool.ExePath} {arguments}", exception);
        }
    }

    public override string ToString() => tool.ToString();
}
