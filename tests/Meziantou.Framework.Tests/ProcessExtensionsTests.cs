using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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
            var result = await ProcessExtensions.RunAsTask("cmd", "/C echo test", CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(1, result.Output.Count);
            Assert.Equal("test", result.Output[0].Text);
            Assert.Equal(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [Fact]
        public async Task RunAsTask_RedirectOutput()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C echo test",
            };

            var result = await psi.RunAsTask(redirectOutput: true, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(1, result.Output.Count);
            Assert.Equal("test", result.Output[0].Text);
            Assert.Equal(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [Fact]
        public async Task RunAsTask_DoNotRedirectOutput()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C echo test",
            };

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
            using var cts = new CancellationTokenSource();
            var task = ProcessExtensions.RunAsTask("cmd", "/C ping 127.0.0.1 -n 10", cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for the process to start
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => task).ConfigureAwait(false);
        }

        [Fact]
        public void GetProcesses()
        {
            var processes = ProcessExtensions.GetProcesses();

            var currentProcess = Process.GetCurrentProcess();
            Assert.True(processes.Any(p => p.ProcessId == currentProcess.Id), "Current process is not in the list of processes");
            AssertExtensions.AllItemsAreUnique(processes.ToList());
        }

        [Fact]
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

        [Fact]
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

        [Fact]
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

        [Fact]
        public void KillProcess_EntireProcessTree_True()
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

                process.Kill(entireProcessTree: true);

                var childProcess = processes.First();
                Assert.True(childProcess.HasExited);
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }
}
