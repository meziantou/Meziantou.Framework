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
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static IEnumerable<Process> GetAncestorProcesses(this Process process)
    {
        Func<int, int?> getParentProcessId;
        if (OperatingSystem.IsWindows())
        {
            var parentProcessIds = new Dictionary<int, int>();
            foreach (var entry in GetProcesses())
            {
                parentProcessIds.TryAdd(entry.ProcessId, entry.ParentProcessId);
            }

            getParentProcessId = processId => parentProcessIds.TryGetValue(processId, out var parentProcessId) ? parentProcessId : null;
        }
        else if (OperatingSystem.IsLinux())
        {
            getParentProcessId = GetLinuxParentProcessId;
        }
        else if (OperatingSystem.IsMacOS())
        {
            getParentProcessId = GetMacOSParentProcessId;
        }
        else
        {
            throw new PlatformNotSupportedException("Only supported on Windows, Linux and macOS");
        }

        return GetAncestorProcessesIterator(process.Id, process.StartTime, getParentProcessId);

        static IEnumerable<Process> GetAncestorProcessesIterator(int processId, DateTime startTime, Func<int, int?> getParentProcessId)
        {
            var visitedProcessIds = new HashSet<int> { processId };
            while (getParentProcessId(processId) is { } parentProcessId && parentProcessId > 0 && visitedProcessIds.Add(parentProcessId))
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

                // A parent process id is only a number, and the operating system reuses it once the parent exits. A process
                // started after the child cannot be its parent, and the processes above it belong to an unrelated chain.
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

    /// <summary>Reads the fourth field of <c>/proc/[pid]/stat</c>.</summary>
    private static int? GetLinuxParentProcessId(int processId)
    {
        string stat;
        try
        {
            stat = File.ReadAllText("/proc/" + processId.ToString(CultureInfo.InvariantCulture) + "/stat");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return ParseLinuxParentProcessId(stat);
    }

    /// <summary>
    /// Parses <c>pid (comm) state ppid ...</c>. The command name can contain spaces and parentheses, so the fields
    /// are read after its last closing parenthesis.
    /// </summary>
    internal static int? ParseLinuxParentProcessId(string stat)
    {
        var commandEnd = stat.AsSpan().LastIndexOf(')');
        if (commandEnd < 0)
            return null;

        var fields = stat.AsSpan(commandEnd + 1).Trim();
        var stateEnd = fields.IndexOf(' ');
        if (stateEnd < 0)
            return null;

        fields = fields[(stateEnd + 1)..];
        var parentProcessIdEnd = fields.IndexOf(' ');
        if (parentProcessIdEnd >= 0)
        {
            fields = fields[..parentProcessIdEnd];
        }

        return int.TryParse(fields, NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId) ? parentProcessId : null;
    }

    [SupportedOSPlatform("macos")]
    private static int? GetMacOSParentProcessId(int processId)
    {
        // struct proc_bsdinfo from <sys/proc_info.h>: pbi_ppid is the fifth uint32_t
        const int ProcPidTBsdInfo = 3;
        const int ProcBsdInfoSize = 136;
        const int ParentProcessIdOffset = 16;

        Span<byte> buffer = stackalloc byte[ProcBsdInfoSize];
        try
        {
            if (ProcPidInfo(processId, ProcPidTBsdInfo, 0, buffer, buffer.Length) != ProcBsdInfoSize)
                return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        return BitConverter.ToInt32(buffer[ParentProcessIdOffset..]);
    }

    [SupportedOSPlatform("macos")]
    [LibraryImport("libproc", EntryPoint = "proc_pidinfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int ProcPidInfo(int pid, int flavor, ulong arg, Span<byte> buffer, int bufferSize);
}
