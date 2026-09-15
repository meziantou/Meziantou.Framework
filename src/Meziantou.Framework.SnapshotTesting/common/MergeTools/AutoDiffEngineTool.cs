using Meziantou.Framework.DiffEngine;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

/// <summary>
/// Starts the first installed diff tool that supports the file, in the order DiffEngine prefers them.
/// </summary>
/// <remarks>
/// Terminal tools (Vim, Neovim) are never picked here. The test host has no terminal they could draw in, so they never
/// show the diff, <c>MergeToolSync</c> waits for them forever, and each failing snapshot leaves one running behind. As
/// <c>vim</c> comes with macOS and most Linux distributions, it would otherwise win over the GUI tools listed after it.
/// A terminal tool is still started when it is named explicitly, with <see cref="MergeTool.Vim" /> or the
/// <c>DiffEngine_Tool</c> environment variable.
/// </remarks>
internal sealed class AutoDiffEngineTool : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        var availableTools = ResolveAvailableTools();
        var selectedTool = SelectTool(FullPath.FromPath(currentFilePath).Extension, availableTools.Select(tool => (tool.Tool, tool.SupportsText, (IEnumerable<string>)tool.BinaryExtensions)));
        if (selectedTool is null)
            return null;

        var resolvedTool = availableTools.First(tool => tool.Tool == selectedTool.Value);
        return DiffEngineTool.Start(resolvedTool, currentFilePath, newFilePath, waitForMerge);
    }

    internal static bool IsTerminalTool(DiffTool tool) => tool is DiffTool.Vim or DiffTool.Neovim;

    /// <summary>
    /// Selects the tool to start among the installed ones, given in order of preference: the first tool dedicated to
    /// the extension of the file, or else the first tool that supports text, as
    /// <see cref="DiffTools.TryFindByExtension(string, out ResolvedTool?)" /> does. Terminal tools are skipped.
    /// </summary>
    internal static DiffTool? SelectTool(string extension, IEnumerable<(DiffTool Tool, bool SupportsText, IEnumerable<string> BinaryExtensions)> availableTools)
    {
        DiffTool? textTool = null;
        foreach (var (tool, supportsText, binaryExtensions) in availableTools)
        {
            if (IsTerminalTool(tool))
                continue;

            if (extension is not "" && binaryExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return tool;

            if (supportsText)
            {
                textTool ??= tool;
            }
        }

        return textTool;
    }

    private static List<ResolvedTool> ResolveAvailableTools()
    {
        var result = new List<ResolvedTool>();

        // The members of DiffTool are declared in the order DiffEngine prefers the tools, the one TryFindByExtension uses.
        foreach (var tool in Enum.GetValues<DiffTool>())
        {
            // A terminal tool is never selected, so looking for its executable is wasted file system probes.
            if (IsTerminalTool(tool))
                continue;

            try
            {
                if (DiffTools.TryFindByName(tool, out var resolvedTool))
                {
                    result.Add(resolvedTool);
                }
            }
            catch (InvalidOperationException)
            {
                // DiffEngine_<Tool> points to an executable that does not exist. TryFindByExtension skips such a tool
                // too, so the other tools can still be used.
            }
        }

        return result;
    }
}
