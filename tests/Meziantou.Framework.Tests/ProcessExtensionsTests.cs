#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests
{
    [Collection("ProcessExtensions")]
    public class ProcessExtensionsTests
    {
        [Fact]
        public async Task RunAsTask()
        {
            static Task<ProcessResult> CreateProcess()
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return ProcessExtensions.RunAsTaskAsync("cmd", "/C echo test", CancellationToken.None);

                return ProcessExtensions.RunAsTaskAsync("echo", "test", CancellationToken.None);
            }

            var result = await CreateProcess().ConfigureAwait(false);
            result.ExitCode.Should().Be(0);
            result.Output.Should().ContainSingle();
            result.Output[0].Text.Should().Be("test");
            result.Output[0].Type.Should().Be(ProcessOutputType.StandardOutput);
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

            var result = await psi.RunAsTaskAsync(redirectOutput: true, CancellationToken.None).ConfigureAwait(false);
            result.ExitCode.Should().Be(0);
            result.Output.Should().ContainSingle();
            result.Output[0].Text.Should().Be("test");
            result.Output[0].Type.Should().Be(ProcessOutputType.StandardOutput);
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

            var result = await psi.RunAsTaskAsync(redirectOutput: false, CancellationToken.None).ConfigureAwait(false);
            result.ExitCode.Should().Be(0);
            result.Output.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsTask_ProcessDoesNotExists()
        {
            var psi = new ProcessStartInfo("ProcessDoesNotExists.exe");

            await new Func<Task>(() => psi.RunAsTaskAsync(CancellationToken.None)).Should().ThrowExactlyAsync<Win32Exception>().ConfigureAwait(false);
        }

        [Fact]
        public async Task RunAsTask_Cancel()
        {
            var stopwatch = Stopwatch.StartNew();

            using var cts = new CancellationTokenSource();
            Task task;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                task = ProcessExtensions.RunAsTaskAsync("ping.exe", "127.0.0.1 -n 10", cts.Token);
            }
            else
            {
                task = ProcessExtensions.RunAsTaskAsync("ping", "127.0.0.1 -c 10", cts.Token);
            }

            // Wait for the process to start
            while (true)
            {
                var processes = Process.GetProcesses();
                if (processes.Any(p => p.ProcessName.EqualsIgnoreCase("ping") || p.ProcessName.EqualsIgnoreCase("ping.exe")))
                    break;

                await Task.Delay(100);

                (stopwatch.Elapsed > TimeSpan.FromSeconds(10)).Should().BeFalse("Cannot find the process");
            }

            cts.Cancel();

            await new Func<Task>(() => task).Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void GetProcesses()
        {
            var processes = ProcessExtensions.GetProcesses();

            var currentProcess = Process.GetCurrentProcess();
            processes.Should().Contain(p => p.ProcessId == currentProcess.Id, "Current process is not in the list of processes");
            processes.Should().OnlyHaveUniqueItems();
        }

        [RunIfFact(FactOperatingSystem.Windows)]
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

                (processes.Count == 1 || processes.Count == 2).Should().BeTrue($"There should be 1 or 2 children (ping and conhost): {string.Join(",", processes.Select(p => p.ProcessName))}");
                processes.Any(p => p.ProcessName.EqualsIgnoreCase("PING") || p.ProcessName.EqualsIgnoreCase("CONHOST")).Should().BeTrue($"PING and CONHOST are not in the child processes: {string.Join(",", processes.Select(p => p.ProcessName))}");
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }


        [RunIfFact(FactOperatingSystem.Windows | FactOperatingSystem.Linux)]
        public void GetParentProcessId()
        {
            var current = Process.GetCurrentProcess();
            var parent = current.GetParentProcessId();

            parent.Should().NotBeNull();
            parent.Should().NotBe(current.Id);
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void GetParent()
        {
            var current = Process.GetCurrentProcess();
            var parent = current.GetParentProcess();
            var grandParent = parent.GetParentProcess();

            grandParent.Should().NotBeNull();

            var descendants = grandParent.GetDescendantProcesses();
            descendants.Should().Contain(p => p.Id == current.Id, "Descendants must contains current process");
            descendants.Should().Contain(p => p.Id == parent.Id, "Descendants must contains parent process");
        }

        [RunIfFact(FactOperatingSystem.Windows)]
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

                (processes.Count == 1 || processes.Count == 2).Should().BeTrue($"There should be 1 or 2 children (ping and conhost): {string.Join(",", processes.Select(p => p.ProcessName))}");

                var childProcess = processes.First();

                process.Kill(entireProcessTree: false);

                childProcess.HasExited.Should().BeFalse();
                childProcess.Kill();
                childProcess.WaitForExit();
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }

        [RunIfFact(FactOperatingSystem.Windows)]
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
                        false.Should().BeTrue("Cannot find the ping process. Running processes: " + string.Join(", ", allProcesses.Select(p => p.ProcessName)));
                    }

                    Thread.Sleep(100);
                    continue;
                }

                pingProcess.Should().NotBeNull();

                ProcessExtensions.Kill(shellProcess, entireProcessTree: true);

                shellProcess.WaitForExit(1000);
                shellProcess.HasExited.Should().BeTrue($"Shell process ({shellProcess.Id.ToStringInvariant()}) has not exited");

                pingProcess.WaitForExit(1000);
                pingProcess.HasExited.Should().BeTrue($"Ping process ({pingProcess.Id.ToStringInvariant()}) has not exited");
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
