using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Utilities
{
    public static class ProcessExtensions
    {
        public static void KillProcessTree(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            var childProcesses = GetChildProcesses(process);
            foreach (var childProcess in childProcesses)
            {
                if (!childProcess.HasExited)
                {
                    childProcess.Kill();
                }
            }

            process.Kill();

            foreach (var childProcess in childProcesses)
            {
                childProcess.WaitForExit();
            }

            process.WaitForExit();
        }

        public static IReadOnlyList<Process> GetChildProcesses(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            var children = new List<Process>();
            GetChildProcesses(process, children);
            return children;
        }

        private static void GetChildProcesses(Process process, List<Process> children)
        {
            IntPtr snapShotHandle = CreateToolhelp32Snapshot(SnapshotFlags.TH32CS_SNAPPROCESS, 0);
            try
            {
                var entry = new ProcessEntry32();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32));

                int result = Process32First(snapShotHandle, ref entry);
                while (result != 0)
                {
                    if (process.Id == entry.th32ParentProcessID)
                    {
                        try
                        {
                            var child = Process.GetProcessById((int)entry.th32ProcessID);
                            children.Add(child);
                            GetChildProcesses(child, children);
                        }
                        catch (ArgumentException)
                        {
                            // process might have exited since the snapshot, ignore it
                        }
                    }

                    result = Process32Next(snapShotHandle, ref entry);
                }
            }
            finally
            {
                if (snapShotHandle != IntPtr.Zero)
                {
                    CloseHandle(snapShotHandle);
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int Process32First(IntPtr handle, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int Process32Next(IntPtr handle, ref ProcessEntry32 entry);

        // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682489.aspx
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);

        private const int MAX_PATH = 260;

        // https://msdn.microsoft.com/en-us/library/windows/desktop/ms684839.aspx
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct ProcessEntry32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szExeFile;
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
    }
}
