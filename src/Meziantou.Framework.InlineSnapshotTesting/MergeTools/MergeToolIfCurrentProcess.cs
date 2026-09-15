using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class MergeToolIfCurrentProcess(MergeTool tool, string[] processNames) : MergeTool
{
    // Windows: devenv, rider64, code. Linux: rider, code. macOS: rider, and the helper processes of Visual Studio Code,
    // such as the extension host that starts the test runner and the host of the integrated terminal.
    internal static readonly string[] VisualStudioProcessNames = ["devenv", "devenv.exe"];
    internal static readonly string[] RiderProcessNames = ["rider64", "rider64.exe", "rider"];
    internal static readonly string[] VisualStudioCodeProcessNames = ["code", "code.exe", "Code Helper", "Code Helper (Plugin)"];

    private static readonly HashSet<string> IdeProcessNames = new([.. VisualStudioProcessNames, .. RiderProcessNames, .. VisualStudioCodeProcessNames], StringComparer.OrdinalIgnoreCase);

    // PublicationOnly: a failure to inspect the process tree is not cached, so the next assertion tries again.
    private static readonly Lazy<string?> CurrentProcessName = new(GetContextProcessName, LazyThreadSafetyMode.PublicationOnly);

    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        var processName = CurrentProcessName.Value;
        if (processName is null)
            return null;

        if (processNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return tool.Start(currentFilePath, newFilePath, waitForMerge);

        return null;
    }

    internal static string? GetContextProcessName()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return null;

        using var currentProcess = Process.GetCurrentProcess();
        foreach (var ancestor in currentProcess.GetAncestorProcesses())
        {
            using (ancestor)
            {
                string processName;
                try
                {
                    processName = ancestor.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    // The process exited since the process tree was read.
                    continue;
                }

                if (IdeProcessNames.Contains(processName))
                    return processName;
            }
        }

        return null;
    }

    public override string ToString() => tool + "IfCurrentProcess";
}
