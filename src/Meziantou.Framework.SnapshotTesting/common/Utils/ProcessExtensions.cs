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
    [SupportedOSPlatform("windows")]
    public static IEnumerable<int> GetAncestorProcessIds(this Process process)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Only supported on Windows");

        return GetAncestorProcessIdsIterator();

        IEnumerable<int> GetAncestorProcessIdsIterator()
        {
            var returnedProcesses = new HashSet<int>();

            var processId = process.Id;
            var processes = GetProcesses().ToList();
            var found = true;
            while (found)
            {
                found = false;
                foreach (var entry in processes)
                {
                    if (entry.ProcessId == processId)
                    {
                        if (returnedProcesses.Add(entry.ParentProcessId))
                        {
                            yield return entry.ParentProcessId;
                            processId = entry.ParentProcessId;
                            found = true;
                        }
                    }
                }

                if (!found)
                    yield break;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public static IEnumerable<Process> GetAncestorProcesses(this Process process)
    {
        return GetAncestorProcesses();

        IEnumerable<Process> GetAncestorProcesses()
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Only supported on Windows");

            foreach (var entry in GetAncestorProcessIdsIterator())
            {
                Process? p = null;
                try
                {
                    p = entry.ToProcess();
                    try
                    {
                        if (p is null || p.StartTime > process.StartTime)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }
                }
                catch (ArgumentException)
                {
                    // process might have exited since the snapshot, ignore it
                }

                if (p is not null)
                    yield return p;
            }

            IEnumerable<ProcessEntry> GetAncestorProcessIdsIterator()
            {
                var returnedProcesses = new HashSet<int>();
                var processId = process.Id;
                var processes = GetProcesses().ToList();
                var found = true;
                while (found)
                {
                    found = false;
                    foreach (var entry in processes)
                    {
                        if (entry.ProcessId == processId)
                        {
                            if (returnedProcesses.Add(entry.ParentProcessId))
                            {
                                yield return entry;
                                processId = entry.ParentProcessId;
                                found = true;
                            }
                        }
                    }

                    if (!found)
                        yield break;
                }
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
