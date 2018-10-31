using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework
{
    public static partial class ProcessExtensions
    {
        // Mark as obsolete when merged https://github.com/dotnet/corefx/pull/31827
        public static void Kill(this Process process, bool entireProcessTree = false)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

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

            foreach (var childProcess in childProcesses)
            {
                childProcess.WaitForExit();
            }

            process.WaitForExit();
        }

        public static IReadOnlyList<Process> GetDescendantProcesses(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!IsWindows())
                throw new PlatformNotSupportedException("Only supported on Windows");

            var children = new List<Process>();
            GetChildProcesses(process, children, int.MaxValue, 0);
            return children;
        }

        public static IReadOnlyList<Process> GetChildProcesses(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!IsWindows())
                throw new PlatformNotSupportedException("Only supported on Windows");

            var children = new List<Process>();
            GetChildProcesses(process, children, 1, 0);
            return children;
        }

        public static Process GetParentProcess(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!IsWindows())
                throw new PlatformNotSupportedException("Only supported on Windows");

            var processId = process.Id;
            foreach (var entry in GetProcesses())
            {
                if (entry.ProcessId == processId)
                {
                    return Process.GetProcessById(entry.ParentProcessId);
                }
            }

            return null;
        }

        public static IEnumerable<ProcessEntry> GetProcesses()
        {
            using (var snapShotHandle = new SnapshotSafeHandle(CreateToolhelp32Snapshot(SnapshotFlags.TH32CS_SNAPPROCESS, 0), ownHandle: true))
            {
                var entry = new ProcessEntry32
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32))
                };

                var result = Process32First(snapShotHandle, ref entry);
                while (result != 0)
                {
                    yield return new ProcessEntry(entry.th32ProcessID, entry.th32ParentProcessID);
                    result = Process32Next(snapShotHandle, ref entry);
                }
            }
        }

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

        private static bool IsWindows()
        {
#if NETSTANDARD2_0
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#elif NET461
            return true;
#else
#error Platform not supported
#endif
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
        private static extern IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);

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
            TH32CS_INHERIT = 0x80000000
        }

        private class SnapshotSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SnapshotSafeHandle(IntPtr handle, bool ownHandle)
                : base(ownHandle)
            {
                SetHandle(handle);
            }

            protected override bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }
    }
}
