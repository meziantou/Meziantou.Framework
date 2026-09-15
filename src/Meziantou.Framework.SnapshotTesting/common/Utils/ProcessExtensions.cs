using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
#else
namespace Meziantou.Framework.SnapshotTesting.Utils;
#endif

internal static partial class ProcessExtensions
{
    /// <summary>
    /// Enumerates the ancestors of <paramref name="process" />, from its parent up. The process itself is not part of the
    /// result. The caller owns, and must dispose, every returned <see cref="Process" />.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IEnumerable<Process> GetAncestorProcesses(this Process process)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Only supported on Windows");

        return GetAncestorProcessesIterator(process.Id, process.StartTime);

        static IEnumerable<Process> GetAncestorProcessesIterator(int processId, DateTime startTime)
        {
            var parentProcessIds = new Dictionary<int, int>();
            foreach (var entry in GetProcesses())
            {
                parentProcessIds.TryAdd(entry.ProcessId, entry.ParentProcessId);
            }

            var visitedProcessIds = new HashSet<int> { processId };
            while (parentProcessIds.TryGetValue(processId, out var parentProcessId) && visitedProcessIds.Add(parentProcessId))
            {
                Process parent;
                try
                {
                    parent = Process.GetProcessById(parentProcessId);
                }
                catch (ArgumentException)
                {
                    // The parent exited, so the rest of the chain cannot be found.
                    yield break;
                }

                DateTime parentStartTime;
                try
                {
                    parentStartTime = parent.StartTime;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    parent.Dispose();
                    yield break;
                }

                // A parent process id is only a number, and Windows reuses it once the parent exits. A process started
                // after the child cannot be its parent, and the processes above it belong to an unrelated chain.
                if (parentStartTime > startTime)
                {
                    parent.Dispose();
                    yield break;
                }

                yield return parent;

                processId = parentProcessId;
                startTime = parentStartTime;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public static IEnumerable<ProcessEntry> GetProcesses()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException("Only supported on Windows");

        using var snapShotHandle = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapShotHandle.IsInvalid)
            yield break;

        var entry = new PROCESSENTRY32W
        {
            dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>(),
        };

        var result = PInvoke.Process32FirstW(snapShotHandle, ref entry);
        while (result)
        {
            yield return new ProcessEntry(unchecked((int)entry.th32ProcessID), unchecked((int)entry.th32ParentProcessID));
            result = PInvoke.Process32NextW(snapShotHandle, ref entry);
        }
    }
}
