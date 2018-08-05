using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.Utilities
{
    public static class ProcessExtensions
    {
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

        public static void KillProcessTree(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!IsWindows())
                throw new PlatformNotSupportedException("Only supported on Windows");

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

            if (!IsWindows())
                throw new PlatformNotSupportedException("Only supported on Windows");

            var children = new List<Process>();
            GetChildProcesses(process, children);
            return children;
        }

        private static void GetChildProcesses(Process process, List<Process> children)
        {
            var snapShotHandle = CreateToolhelp32Snapshot(SnapshotFlags.TH32CS_SNAPPROCESS, 0);
            try
            {
                var entry = new ProcessEntry32
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32))
                };

                var result = Process32First(snapShotHandle, ref entry);
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

        public static Task<ProcessResult> RunAsTask(string fileName, string arguments, CancellationToken cancellationToken = default)
        {
            return RunAsTask(fileName, arguments, null, cancellationToken);
        }

        public static Task<ProcessResult> RunAsTask(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                ErrorDialog = false,
                UseShellExecute = false
            };

            return RunAsTask(psi, cancellationToken);
        }

        public static Task<ProcessResult> RunAsTask(this ProcessStartInfo psi, bool redirectOutput, CancellationToken cancellationToken = default)
        {
            if (redirectOutput)
            {
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
            }
            else
            {
                psi.RedirectStandardError = false;
                psi.RedirectStandardOutput = false;
            }

            return RunAsTask(psi, cancellationToken);
        }

        public static Task<ProcessResult> RunAsTask(this ProcessStartInfo psi, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<ProcessResult>(cancellationToken);

            var tcs = new TaskCompletionSource<ProcessResult>();
            var logs = new List<ProcessOutput>();

            var process = new Process();
            process.StartInfo = psi;
            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) =>
            {
                process.WaitForExit();
                tcs.SetResult(new ProcessResult(process.ExitCode, logs));
                process.Dispose();
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    logs.Add(new ProcessOutput(ProcessOutputType.StandardError, e.Data));
                }
            };
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    logs.Add(new ProcessOutput(ProcessOutputType.StandardOutput, e.Data));
                }
            };

            if (!process.Start())
                throw new InvalidOperationException($"Cannot start the process '{psi.FileName}'");

            if (psi.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (psi.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            cancellationToken.Register(() =>
            {
                if (process.HasExited)
                    return;

                try
                {
                    if (IsWindows())
                    {
                        process.KillProcessTree();
                    }
                    else
                    {
                        process.Kill();
                    }
                }
                finally
                {
                    process.Dispose();
                }
            });

            if (psi.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

            return tcs.Task;
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
#pragma warning disable IDE1006 // Naming Styles
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
    }
}
