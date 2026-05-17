using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace Meziantou.Framework;

public static partial class ProcessExtensions
{
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Process> GetDescendantProcesses(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Only supported on Windows");

        var children = new List<Process>();
        GetChildProcesses(process, children, int.MaxValue, 0);
        return children;
    }

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Process> GetChildProcesses(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Only supported on Windows");

        var children = new List<Process>();
        GetChildProcesses(process, children, 1, 0);
        return children;
    }

    [SupportedOSPlatform("windows")]
    public static IEnumerable<int> GetAncestorProcessIds(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

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
        ArgumentNullException.ThrowIfNull(process);

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
                    if (p is null || p.StartTime > process.StartTime)
                        continue;
                }
                catch (ArgumentException)
                {
                    // process might have exited since the snapshot, ignore it
                }

                if (p is not null)
                {
                    yield return p;
                }
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
    [SupportedOSPlatform("linux")]
    public static int? GetParentProcessId(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (OperatingSystem.IsWindows())
        {
            var processId = process.Id;
            foreach (var entry in GetProcesses())
            {
                if (entry.ProcessId == processId)
                {
                    return entry.ParentProcessId;
                }
            }
        }
        else
        {
            var processId = process.Id;
            try
            {
                using var stream = File.OpenRead("/proc/" + processId.ToStringInvariant() + "/status");
                using var sr = new StreamReader(stream);
                string? line;
                while ((line = sr.ReadLine()) is not null)
                {
                    const string Prefix = "PPid:";
                    if (line.StartsWith(Prefix, StringComparison.Ordinal))
                    {
                        if (int.TryParse(line[Prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ppid))
                            return ppid;
                    }
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    public static Process? GetParentProcess(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var parentProcessId = GetParentProcessId(process);
        if (parentProcessId is null)
            return null;

        var parentProcess = Process.GetProcessById(parentProcessId.Value);
        if (parentProcess is null || parentProcess.StartTime > process.StartTime)
            return null;

        return parentProcess;
    }

    [SupportedOSPlatform("windows")]
    public static IEnumerable<ProcessEntry> GetProcesses()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Only supported on Windows");

        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException("Only supported on Windows");

        using var snapShotHandle = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        var entry = new PROCESSENTRY32
        {
            dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>(),
        };

        var result = PInvoke.Process32First(snapShotHandle, ref entry);
        while (result)
        {
            yield return new ProcessEntry(unchecked((int)entry.th32ProcessID), unchecked((int)entry.th32ParentProcessID));
            result = PInvoke.Process32Next(snapShotHandle, ref entry);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void GetChildProcesses(Process process, List<Process> children, int maxDepth, int currentDepth)
    {
        ArgumentNullException.ThrowIfNull(process);

        var entries = new List<ProcessEntry>(100);
        foreach (var entry in GetProcesses())
        {
            entries.Add(entry);
        }

        GetChildProcesses(entries, process, children, maxDepth, currentDepth);
    }

    private static void GetChildProcesses(List<ProcessEntry> entries, Process process, List<Process> children, int maxDepth, int currentDepth)
    {
        var processId = process.Id;
        foreach (var entry in entries)
        {
            if (entry.ParentProcessId == processId)
            {
                try
                {
                    var child = entry.ToProcess();
                    if (child is null || child.StartTime < process.StartTime)
                        continue;

                    children.Add(child);
                    if (currentDepth < maxDepth)
                    {
                        GetChildProcesses(entries, child, children, maxDepth, currentDepth + 1);
                    }
                }
                catch (ArgumentException)
                {
                    // process might have exited since the snapshot, ignore it
                }
            }
        }
    }

}
