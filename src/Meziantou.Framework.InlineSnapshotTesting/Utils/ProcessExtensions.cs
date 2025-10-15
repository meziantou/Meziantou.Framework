using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.InlineSnapshotTesting.Utils;

internal static partial class ProcessExtensions
{
    private static bool IsWindows()
    {
#if NET5_0_OR_GREATER
        return OperatingSystem.IsWindows();
#else
        return Environment.OSVersion.Platform is PlatformID.Win32NT;
#endif
    }

    [SupportedOSPlatform("windows")]
    public static IEnumerable<int> GetAncestorProcessIds(this Process process)
    {
        if (!IsWindows())
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
            if (!IsWindows())
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
        if (!IsWindows())
            throw new PlatformNotSupportedException("Only supported on Windows");

        using var snapShotHandle = CreateToolhelp32Snapshot(SnapshotFlags.TH32CS_SNAPPROCESS, 0);
        var entry = new ProcessEntry32
        {
            dwSize = (uint)Marshal.SizeOf<ProcessEntry32>(),
        };

        var result = Process32First(snapShotHandle, ref entry);
        while (result != 0)
        {
            yield return new ProcessEntry(entry.th32ProcessID, entry.th32ParentProcessID);
            result = Process32Next(snapShotHandle, ref entry);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Process32First(SnapshotSafeHandle handle, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Process32Next(SnapshotSafeHandle handle, ref ProcessEntry32 entry);

    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682489.aspx
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SnapshotSafeHandle CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);

    private const int MAX_PATH = 260;

    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms684839.aspx
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct ProcessEntry32
    {
#pragma warning disable IDE1006 // Naming Styles
        public uint dwSize;
        public uint cntUsage;
        public int th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public int th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string szExeFile;
#pragma warning restore IDE1006 // Naming Styles
    }

    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682489.aspx
    private enum SnapshotFlags : uint
    {
        TH32CS_SNAPHEAPLIST = 0x00000001,
        TH32CS_SNAPPROCESS = 0x00000002,
        TH32CS_SNAPTHREAD = 0x00000004,
        TH32CS_SNAPMODULE = 0x00000008,
        TH32CS_SNAPMODULE32 = 0x00000010,
        TH32CS_INHERIT = 0x80000000,
    }

    private sealed class SnapshotSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SnapshotSafeHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }
}
