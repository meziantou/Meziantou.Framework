using System.ComponentModel;
using System.Diagnostics;
using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

[Collection("ProcessExtensions")]
public class ProcessExtensionsTests
{
    [Fact]
    public async Task RunAsTask()
    {
        static ProcessWrapper CreateProcess()
        {
            if (OperatingSystem.IsWindows())
            {
                return ProcessWrapper.Create("cmd")
                    .WithArguments("/C", "echo test");
            }

            return ProcessWrapper.Create("echo")
                .WithArguments("test");
        }

        var result = await CreateProcess()
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Output.StandardOutput);
        Assert.Equal("test", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task RunAsTask_RedirectOutput()
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
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

        var result = await ProcessWrapper.Create(psi.FileName)
            .WithArguments(psi.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Output.StandardOutput);
        Assert.Equal("test", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task RunAsTask_DoNotRedirectOutput()
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
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

        var result = await ProcessWrapper.Create(psi.FileName)
            .WithArguments(psi.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteAsync();

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsTask_ProcessDoesNotExists()
    {
        await Assert.ThrowsAsync<Win32Exception>(async () =>
        {
            var process = ProcessWrapper.Create("ProcessDoesNotExists.exe")
                .WithValidation(ProcessValidationMode.None)
                .ExecuteAsync(CancellationToken.None);

            await process;
        });
    }

    [Fact]
    public async Task RunAsTask_Cancel()
    {
        var stopwatch = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource();
        ProcessInstance task;
        if (OperatingSystem.IsWindows())
        {
            task = ProcessWrapper.Create("ping.exe")
                .WithArguments("127.0.0.1", "-n", "10")
                .WithValidation(ProcessValidationMode.None)
                .ExecuteAsync(cts.Token);
        }
        else
        {
            task = ProcessWrapper.Create("ping")
                .WithArguments("127.0.0.1", "-c", "10")
                .WithValidation(ProcessValidationMode.None)
                .ExecuteAsync(cts.Token);
        }

        // Wait for the process to start
        while (true)
        {
            var processes = Process.GetProcesses();
            if (processes.Any(p => p.ProcessName.EqualsIgnoreCase("ping") || p.ProcessName.EqualsIgnoreCase("ping.exe")))
                break;

            await Task.Delay(100, cts.Token);
            Assert.False(stopwatch.Elapsed > TimeSpan.FromSeconds(10));
        }

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetProcesses()
    {
        var processes = ProcessExtensions.GetProcesses().ToArray();

        var currentProcess = Process.GetCurrentProcess();
        Assert.Contains(processes, p => p.ProcessId == currentProcess.Id);
        Assert.Equal(processes.Distinct().OrderBy(p => p.ProcessId), processes.OrderBy(p => p.ProcessId)); // items must be unique
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetDescendantProcesses()
    {
        using var process = Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10");
        try
        {
            // We need to wait for the process to be started by cmd
            IReadOnlyCollection<Process> processes;
            while ((processes = process.GetDescendantProcesses()).Count is 0)
            {
                Thread.Sleep(100);
                continue;
            }

            Assert.True(processes.Count is 1 or 2);
            Assert.Contains(processes, p => p.ProcessName.EqualsIgnoreCase("PING") || p.ProcessName.EqualsIgnoreCase("CONHOST"));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux)]
    public void GetParentProcessId()
    {
        var current = Process.GetCurrentProcess();
        var parent = current.GetParentProcessId();

        Assert.NotNull(parent);
        Assert.NotEqual(current.Id, parent);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetParent()
    {
        var current = Process.GetCurrentProcess();
        var parent = current.GetParentProcess();
        Assert.NotNull(parent);
        var grandParent = parent.GetParentProcess();

        Assert.NotNull(grandParent);

        var descendants = grandParent.GetDescendantProcesses();
        Assert.Contains(descendants, p => p.Id == current.Id);
        Assert.Contains(descendants, p => p.Id == parent.Id);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetAncestorProcesses_StartsWithTheParent()
    {
        using var current = Process.GetCurrentProcess();
        var ancestors = current.GetAncestorProcesses().ToList();
        try
        {
            Assert.NotEmpty(ancestors);
            Assert.Equal(current.GetParentProcessId(), ancestors[0].Id);
            Assert.DoesNotContain(ancestors, p => p.Id == current.Id);
            for (var i = 1; i < ancestors.Count; i++)
            {
                Assert.True(ancestors[i].StartTime <= ancestors[i - 1].StartTime);
            }
        }
        finally
        {
            foreach (var ancestor in ancestors)
            {
                ancestor.Dispose();
            }
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetParentProcess_ParentExited_ReturnsNull()
    {
        // cmd.exe starts a long-running ping in the background, then exits, leaving the ping with the id of an exited parent
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/C start /B ping 127.0.0.1 -n 30 >NUL & ping 127.0.0.1 -n 3 >NUL") { UseShellExecute = false, CreateNoWindow = true });
        Assert.NotNull(process);
        IReadOnlyList<Process> descendants = [];
        try
        {
            while (true)
            {
                descendants = process.GetDescendantProcesses();
                if (descendants.Count(p => p.ProcessName.EqualsIgnoreCase("PING")) is 2 || process.HasExited)
                    break;

                foreach (var descendant in descendants)
                {
                    descendant.Dispose();
                }

                Thread.Sleep(50);
            }

            process.WaitForExit();

            var orphan = Assert.Single(descendants, p => p.ProcessName.EqualsIgnoreCase("PING") && !p.HasExited);
            Assert.Equal(process.Id, orphan.GetParentProcessId());
            Assert.Null(orphan.GetParentProcess());
        }
        finally
        {
            foreach (var descendant in descendants)
            {
                try
                {
                    descendant.Kill();
                }
                catch (InvalidOperationException)
                {
                }

                descendant.Dispose();
            }
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetChildProcessesDoesNotReturnGrandChildren()
    {
        using var process = Process.Start("cmd.exe", "/C ping 127.0.0.1 -n 10");
        try
        {
            // We need to wait for the process to be started by cmd
            IReadOnlyCollection<Process> grandChildren;
            while ((grandChildren = process.GetDescendantProcesses()).Count is 0)
            {
                Thread.Sleep(100);
            }

            using var current = Process.GetCurrentProcess();
            var children = current.GetChildProcesses();

            Assert.Contains(children, p => p.Id == process.Id);
            Assert.DoesNotContain(children, p => grandChildren.Any(grandChild => grandChild.Id == p.Id));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [Fact]
    public void Documentation_ParentProcess()
    {
        #region ProcessExtensions_ParentProcess
        using var currentProcess = Process.GetCurrentProcess();
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            int? parentId = currentProcess.GetParentProcessId();
            Assert.NotNull(parentId);

            using var parent = currentProcess.GetParentProcess();
            Assert.NotNull(parent);
        }
        #endregion
    }
}
