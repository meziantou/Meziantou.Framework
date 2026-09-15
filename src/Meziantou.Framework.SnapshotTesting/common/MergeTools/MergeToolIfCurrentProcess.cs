using System.Diagnostics;
#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

internal sealed class MergeToolIfCurrentProcess(MergeTool tool, string[] processNames) : MergeTool
{
    private static readonly HashSet<string> IdeProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "devenv.exe", "devenv",
        "rider64.exe", "rider64",
        "code.exe", "code",
    };

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

    private static string? GetContextProcessName()
    {
        if (!OperatingSystem.IsWindows())
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
