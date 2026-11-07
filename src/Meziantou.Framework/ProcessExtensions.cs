using System.ComponentModel;
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

        return GetChildProcesses(process, int.MaxValue);
    }

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Process> GetChildProcesses(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Only supported on Windows");

        return GetChildProcesses(process, 1);
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

            // A process starts after its parent. When a parent id refers to a process that started later, the parent
            // exited and its id was reused by an unrelated process, so the rest of the chain is not an ancestor.
            var childStartTime = process.StartTime;
            foreach (var parentProcessId in GetAncestorProcessIds(process))
            {
                var parent = TryGetProcessById(parentProcessId);
                if (parent is null)
                    continue; // The process exited since the snapshot

                var parentStartTime = TryGetStartTime(parent);
                if (parentStartTime is null || parentStartTime > childStartTime)
                {
                    parent.Dispose();
                    yield break;
                }

                childStartTime = parentStartTime.Value;
                yield return parent;
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

        var startTime = process.StartTime;
        var parentProcess = TryGetProcessById(parentProcessId.Value);
        if (parentProcess is null)
            return null;

        // A parent that started after the process means the parent exited and its id was reused
        var parentStartTime = TryGetStartTime(parentProcess);
        if (parentStartTime is null || parentStartTime > startTime)
        {
            parentProcess.Dispose();
            return null;
        }

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
    private static List<Process> GetChildProcesses(Process process, int maxDepth)
    {
        var entries = GetProcesses().ToList();
        var startTime = process.StartTime;
        var children = new List<Process>();
        try
        {
            GetChildProcesses(entries, process.Id, startTime, children, maxDepth, currentDepth: 0);
        }
        catch
        {
            foreach (var child in children)
            {
                child.Dispose();
            }

            throw;
        }

        return children;
    }

    private static void GetChildProcesses(List<ProcessEntry> entries, int processId, DateTime startTime, List<Process> children, int maxDepth, int currentDepth)
    {
        foreach (var entry in entries)
        {
            if (entry.ParentProcessId != processId)
                continue;

            var child = TryGetProcessById(entry.ProcessId);
            if (child is null)
                continue; // The process exited since the snapshot

            // A child that started before the process is an unrelated process whose parent id was reused
            var childStartTime = TryGetStartTime(child);
            if (childStartTime is null || childStartTime < startTime)
            {
                child.Dispose();
                continue;
            }

            children.Add(child);

            // currentDepth is the depth of the children being collected, so descending is only
            // allowed while the next level still fits within maxDepth
            if (currentDepth + 1 < maxDepth)
            {
                GetChildProcesses(entries, child.Id, childStartTime.Value, children, maxDepth, currentDepth + 1);
            }
        }
    }

    private static Process? TryGetProcessById(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            // The process is not running
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static DateTime? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (InvalidOperationException)
        {
            // The process exited
            return null;
        }
        catch (Win32Exception)
        {
            // The process exited, or its information cannot be read, e.g. access denied
            return null;
        }
    }
}
