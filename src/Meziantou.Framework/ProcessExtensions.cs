using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework
{
    public static partial class ProcessExtensions
    {
        [Obsolete("Already implemented in .NET 3.1")]
        public static void Kill(this Process process, bool entireProcessTree = false)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

#if NETCOREAPP3_1 || NET5_0
            process.Kill(entireProcessTree);
#elif NETSTANDARD2_0 || NET461
            if (!entireProcessTree)
            {
                process.Kill();
                return;
            }

            if (!IsWindows())
                throw new PlatformNotSupportedException("Only supported on Windows");

            var childProcesses = GetDescendantProcesses(process);

            if (!process.HasExited)
            {
                process.Kill();
            }

            foreach (var childProcess in childProcesses)
            {
                if (!childProcess.HasExited)
                {
                    if (!childProcess.HasExited)
                    {
                        childProcess.Kill();
                    }
                }
            }
#else
#error Platform not supported
#endif
        }

        [SupportedOSPlatform("windows")]
        public static IReadOnlyList<Process> GetDescendantProcesses(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("Only supported on Windows");

            var children = new List<Process>();
            GetChildProcesses(process, children, int.MaxValue, 0);
            return children;
        }

        [SupportedOSPlatform("windows")]
        public static IReadOnlyList<Process> GetChildProcesses(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("Only supported on Windows");

            var children = new List<Process>();
            GetChildProcesses(process, children, 1, 0);
            return children;
        }

        [SupportedOSPlatform("windows")]
        public static IEnumerable<int> GetAncestorProcessIds(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            return GetAncestorProcesses();

            IEnumerable<Process> GetAncestorProcesses()
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw new PlatformNotSupportedException("Only supported on Windows");

                foreach (var entry in GetAncestorProcessIdsIterator())
                {
                    Process? p = null;
                    try
                    {
                        p = entry.ToProcess();
                        if (p == null || p.StartTime > process.StartTime)
                            continue;
                    }
                    catch (ArgumentException)
                    {
                        // process might have exited since the snapshot, ignore it
                    }

                    if (p != null)
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

        public static int? GetParentProcessId(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
                    while ((line = sr.ReadLine()) != null)
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

        public static Process? GetParentProcess(this Process process)
        {
            var parentProcessId = GetParentProcessId(process);
            if (parentProcessId == null)
                return null;

            var parentProcess = Process.GetProcessById(parentProcessId.Value);
            if (parentProcess == null || parentProcess.StartTime > process.StartTime)
                return null;

            return parentProcess;
        }

        [SupportedOSPlatform("windows")]
        public static IEnumerable<ProcessEntry> GetProcesses()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("Only supported on Windows");

            using var snapShotHandle = CreateToolhelp32Snapshot(SnapshotFlags.TH32CS_SNAPPROCESS, 0);
            var entry = new ProcessEntry32
            {
                dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32)),
            };

            var result = Process32First(snapShotHandle, ref entry);
            while (result != 0)
            {
                yield return new ProcessEntry(entry.th32ProcessID, entry.th32ParentProcessID);
                result = Process32Next(snapShotHandle, ref entry);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void GetChildProcesses(Process process, List<Process> children, int maxDepth, int currentDepth)
        {
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
                        if (child == null || child.StartTime < process.StartTime)
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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int Process32First(SnapshotSafeHandle handle, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int Process32Next(SnapshotSafeHandle handle, ref ProcessEntry32 entry);

        // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682489.aspx
        [DllImport("kernel32.dll", SetLastError = true)]
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
}
