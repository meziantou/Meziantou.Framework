﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ProcessExtensionsTests
    {
        [Fact]
        public async Task RunAsTask()
        {
            static Task<ProcessResult> CreateProcess()
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return ProcessExtensions.RunAsTask("cmd", "/C echo test", CancellationToken.None);
                }

                return ProcessExtensions.RunAsTask("echo", "test", CancellationToken.None);
            }

            var result = await CreateProcess().ConfigureAwait(false);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(1, result.Output.Count);
            Assert.Equal("test", result.Output[0].Text);
            Assert.Equal(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [Fact]
        public async Task RunAsTask_RedirectOutput()
        {
            ProcessStartInfo psi;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/C echo test",
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "echo",
                    Arguments = "test",
                };
            }

            var result = await psi.RunAsTask(redirectOutput: true, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(1, result.Output.Count);
            Assert.Equal("test", result.Output[0].Text);
            Assert.Equal(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [Fact]
        public async Task RunAsTask_DoNotRedirectOutput()
        {
            ProcessStartInfo psi;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/C echo test",
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "echo",
                    Arguments = "test",
                };
            }

            var result = await psi.RunAsTask(redirectOutput: false, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(0, result.Output.Count);
        }

        [Fact]
        public async Task RunAsTask_ProcessDoesNotExists()
        {
            var psi = new ProcessStartInfo("ProcessDoesNotExists.exe");

            await Assert.ThrowsAsync<Win32Exception>(() => psi.RunAsTask(CancellationToken.None)).ConfigureAwait(false);
        }

        [Fact]
        public async Task RunAsTask_Cancel()
        {
            DateTime start = DateTime.Now;

            using var cts = new CancellationTokenSource();
            Task task;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                task = ProcessExtensions.RunAsTask("ping.exe", "127.0.0.1 -n 10", cts.Token);
            }
            else
            {
                task = ProcessExtensions.RunAsTask("ping", "127.0.0.1 -c 10", cts.Token);
            }

            await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for the process to start
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => task).ConfigureAwait(false);
        }

        [RunIfWindowsFact]
        public void GetProcesses()
        {
            var processes = ProcessExtensions.GetProcesses();

            var currentProcess = Process.GetCurrentProcess();
            Assert.True(processes.Any(p => p.ProcessId == currentProcess.Id), "Current process is not in the list of processes");
            AssertExtensions.AllItemsAreUnique(processes.ToList());
        }

        [Fact(Skip = "fail on CI")]
        public void GetDescendantProcesses()
        {
            using var process = Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10");
            try
            {
                // We need to wait for the process to be started by cmd
                IReadOnlyCollection<Process> processes;
                while ((processes = process.GetDescendantProcesses()).Count == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                Assert.True(processes.Count == 1 || processes.Count == 2, $"There should be 1 or 2 children (ping and conhost): {string.Join(",", processes.Select(p => p.ProcessName))}");
                Assert.True(processes.Any(p => p.ProcessName.EqualsIgnoreCase("PING") || p.ProcessName.EqualsIgnoreCase("CONHOST")), $"PING and CONHOST are not in the child processes: {string.Join(",", processes.Select(p => p.ProcessName))}");
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }

        [Fact]
        public void GetParentProcessId()
        {
            var current = Process.GetCurrentProcess();
            var parent = current.GetParentProcessId();

            Assert.NotNull(parent);
            Assert.NotEqual(current.Id, parent);
        }

        [RunIfWindowsFact]
        public void GetParent()
        {
            var current = Process.GetCurrentProcess();
            var parent = current.GetParentProcess();
            var grandParent = parent.GetParentProcess();

            Assert.NotNull(grandParent);

            var descendants = grandParent.GetDescendantProcesses();
            Assert.True(descendants.Any(p => p.Id == current.Id), "Descendants must contains current process");
            Assert.True(descendants.Any(p => p.Id == parent.Id), "Descendants must contains parent process");
        }

        [RunIfWindowsAdministratorFact]
        public void GetAncestorProcessIds()
        {
            var current = Process.GetCurrentProcess();
            var parents = current.GetAncestorProcessIds().ToList();

            AssertExtensions.AllItemsAreUnique(parents);
            bool hasParent = false;
            foreach (var parentId in parents)
            {
                try
                {
                    var parent = Process.GetProcessById(parentId);
                    hasParent = true;
                    Assert.True(parent.GetDescendantProcesses().Any(p => p.Id == current.Id), "Parent process must have the current process as descendant");
                }
                catch (ArgumentException)
                {
                }
            }

            Assert.True(hasParent, "The process has no parents");
        }

        [Fact(Skip = "fail on CI")]
        public void KillProcess_EntireProcessTree_False()
        {
            using var process = Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10");
            try
            {
                // We need to wait for the process to be started by cmd
                IReadOnlyCollection<Process> processes;
                while ((processes = process.GetChildProcesses()).Count == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                Assert.True(processes.Count == 1 || processes.Count == 2, $"There should be 1 or 2 children (ping and conhost): {string.Join(",", processes.Select(p => p.ProcessName))}");

                var childProcess = processes.First();

                process.Kill(entireProcessTree: false);

                Assert.False(childProcess.HasExited);
                childProcess.Kill();
                childProcess.WaitForExit();
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }

        [RunIfWindowsFact]
        public void KillProcess_EntireProcessTree_True()
        {
            var start = DateTime.UtcNow;

            static Process CreateProcess()
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10");
                }

                return Process.Start("sh", "-c \"ping 127.0.0.1 -c 10\"");
            }

            Process GetPingProcess()
            {
                var processes = Process.GetProcesses();
                var pingProcesses = processes.Where(p => p.ProcessName.EqualsIgnoreCase("ping")).ToList();
                return pingProcesses.SingleOrDefault(p => p.StartTime >= start.ToLocalTime());
            }

            using var shellProcess = CreateProcess();
            Process pingProcess = null;
            try
            {
                // We need to wait for the process to be started by cmd
                while ((pingProcess = GetPingProcess()) == null)
                {
                    // Must be greater than the ping time to prevent other tests from using this ping instance
                    if (DateTime.UtcNow - start > TimeSpan.FromSeconds(15))
                    {
                        var allProcesses = Process.GetProcesses();
                        Assert.True(false, "Cannot find the ping process. Running processes: " + string.Join(", ", allProcesses.Select(p => p.ProcessName)));
                    }

                    Thread.Sleep(100);
                    continue;
                }

                Assert.NotNull(pingProcess);

                ProcessExtensions.Kill(shellProcess, entireProcessTree: true);

                shellProcess.WaitForExit(1000);
                Assert.True(shellProcess.HasExited, $"Shell process ({shellProcess.Id}) has not exited");

                pingProcess.WaitForExit(1000);
                Assert.True(pingProcess.HasExited, $"Ping process ({pingProcess.Id}) has not exited");
            }
            finally
            {
                if (pingProcess != null)
                {
                    pingProcess.Kill(entireProcessTree: true);
                    pingProcess.WaitForExit();
                }

                shellProcess.Kill(entireProcessTree: true);
                shellProcess.WaitForExit();
            }
        }
    }
}
