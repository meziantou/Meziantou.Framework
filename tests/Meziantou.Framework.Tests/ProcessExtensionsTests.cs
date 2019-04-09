using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class ProcessExtensionsTests
    {
        [TestMethod]
        public async Task RunAsTask()
        {
            var result = await ProcessExtensions.RunAsTask("cmd", "/C echo test", CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual(1, result.Output.Count);
            Assert.AreEqual("test", result.Output[0].Text);
            Assert.AreEqual(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [TestMethod]
        public async Task RunAsTask_RedirectOutput()
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/C echo test";

            var result = await psi.RunAsTask(redirectOutput: true, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual(1, result.Output.Count);
            Assert.AreEqual("test", result.Output[0].Text);
            Assert.AreEqual(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [TestMethod]
        public async Task RunAsTask_DoNotRedirectOutput()
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/C echo test";

            var result = await psi.RunAsTask(redirectOutput: false, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual(0, result.Output.Count);
        }

        [TestMethod]
        public async Task RunAsTask_ProcessDoesNotExists()
        {
            var psi = new ProcessStartInfo("ProcessDoesNotExists.exe");

            await Assert.ThrowsExceptionAsync<Win32Exception>(() => psi.RunAsTask(CancellationToken.None)).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task RunAsTask_Cancel()
        {
            using (var cts = new CancellationTokenSource())
            {
                var task = ProcessExtensions.RunAsTask("cmd", "/C ping 127.0.0.1 -n 10", cts.Token);
                await Task.Delay(TimeSpan.FromSeconds(1));
                cts.Cancel();

                await  Assert.ThrowsExceptionAsync<TaskCanceledException>(() => task).ConfigureAwait(false);
            }
        }

        [TestMethod]
        public void GetProcesses()
        {
            var processes = ProcessExtensions.GetProcesses();

            var currentProcess = Process.GetCurrentProcess();
            Assert.IsTrue(processes.Any(p => p.ProcessId == currentProcess.Id), "Current process is not in the list of processes");
            CollectionAssert.AllItemsAreUnique(processes.ToList());
        }

        [TestMethod]
        public void GetDescendantProcesses()
        {
            using (var process = Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10"))
            {
                try
                {
                    // We need to wait for the process to be started by cmd
                    IReadOnlyCollection<Process> processes;
                    while ((processes = process.GetDescendantProcesses()).Count == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    Assert.IsTrue(processes.Count == 1 || processes.Count == 2, $"There should be 1 or 2 children (ping and conhost): {string.Join(",", processes.Select(p => p.ProcessName))}");
                    Assert.IsTrue(processes.Any(p => p.ProcessName.EqualsIgnoreCase("PING") || p.ProcessName.EqualsIgnoreCase("CONHOST")), $"PING and CONHOST are not in the child processes: {string.Join(",", processes.Select(p => p.ProcessName))}");
                }
                finally
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
        }

        [TestMethod]
        public void GetParent()
        {
            var current = Process.GetCurrentProcess();
            var parent = current.GetParentProcess();
            var grandParent = parent.GetParentProcess();

            Assert.IsNotNull(grandParent);

            var descendants = grandParent.GetDescendantProcesses();
            Assert.IsTrue(descendants.Any(p => p.Id == current.Id), "Descendants must contains current process");
            Assert.IsTrue(descendants.Any(p => p.Id == parent.Id), "Descendants must contains parent process");
        }

        [TestMethod]
        public void GetAncestorProcessIds()
        {
            var current = Process.GetCurrentProcess();
            var parents = current.GetAncestorProcessIds().ToList();

            CollectionAssert.AllItemsAreUnique(parents);
            bool hasParent = false;
            foreach (var parentId in parents)
            {
                try
                {
                    var parent = Process.GetProcessById(parentId);
                    hasParent = true;
                    Assert.IsTrue(parent.GetDescendantProcesses().Any(p => p.Id == current.Id), "Parent process must have the current process as descendant");
                }
                catch (ArgumentException)
                {
                }
            }

            Assert.IsTrue(hasParent, "The process has no parents");
        }

        [TestMethod]
        public void KillProcess_EntireProcessTree_False()
        {
            using (var process = Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10"))
            {
                try
                {
                    // We need to wait for the process to be started by cmd
                    IReadOnlyCollection<Process> processes;
                    while ((processes = process.GetChildProcesses()).Count == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    Assert.IsTrue(processes.Count == 1 || processes.Count == 2, $"There should be 1 or 2 children (ping and conhost): {string.Join(",", processes.Select(p => p.ProcessName))}");

                    var childProcess = processes.First();

                    process.Kill(entireProcessTree: false);

                    Assert.IsFalse(childProcess.HasExited);
                    childProcess.Kill();
                    childProcess.WaitForExit();
                }
                finally
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
        }

        [TestMethod]
        public void KillProcess_EntireProcessTree_True()
        {
            using (var process = Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10"))
            {
                try
                {
                    // We need to wait for the process to be started by cmd
                    IReadOnlyCollection<Process> processes;
                    while ((processes = process.GetChildProcesses()).Count == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    Assert.IsTrue(processes.Count == 1 || processes.Count == 2, $"There should be 1 or 2 children (ping and conhost): {string.Join(",", processes.Select(p => p.ProcessName))}");

                    process.Kill(entireProcessTree: true);

                    var childProcess = processes.First();
                    Assert.IsTrue(childProcess.HasExited);
                }
                finally
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
        }
    }
}
