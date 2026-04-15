using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class MergeToolIfCurrentProcess(MergeTool tool, string[] processNames) : MergeTool
{
    private static readonly HashSet<string> IdeProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "devenv.exe", "devenv",
        "rider64.exe", "rider64",
        "code.exe", "code",
    };

    private static readonly Lazy<string?> CurrentProcessName = new(GetContextProcessName);

    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        var processName = CurrentProcessName.Value;
        if (processName is null)
            return null;

        if (processNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return tool.Start(currentFilePath, newFilePath);

        return null;
    }

    private static string? GetContextProcessName()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        return Process.GetCurrentProcess().GetAncestorProcesses()
            .Select(static process => process.ProcessName)
            .FirstOrDefault(IdeProcessNames.Contains);
    }
}
