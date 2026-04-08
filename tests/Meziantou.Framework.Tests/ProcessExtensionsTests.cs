#pragma warning disable MEZ_NETCORE3_1
using System.ComponentModel;
using System.Diagnostics;
using TestUtilities;

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

        using var result = CreateProcess()
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync();

        var exitCode = await result;
        Assert.Equal(0, exitCode);
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

        using var result = ProcessWrapper.Create(psi.FileName)
            .WithArguments(psi.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync();

        var exitCode = await result;
        Assert.Equal(0, exitCode);
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

        using var process = ProcessWrapper.Create(psi.FileName)
            .WithArguments(psi.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteAsync();

        var exitCode = await process;
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsTask_ProcessDoesNotExists()
    {
        await Assert.ThrowsAsync<Win32Exception>(async () =>
        {
            using var process = ProcessWrapper.Create("ProcessDoesNotExists.exe")
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

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void GetProcesses()
    {
        var processes = ProcessExtensions.GetProcesses().ToArray();

        var currentProcess = Process.GetCurrentProcess();
        Assert.Contains(processes, p => p.ProcessId == currentProcess.Id);
        Assert.Equal(processes.Distinct().OrderBy(p => p.ProcessId), processes.OrderBy(p => p.ProcessId)); // items must be unique
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
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

    [Fact, RunIf(FactOperatingSystem.Windows | FactOperatingSystem.Linux)]
    public void GetParentProcessId()
    {
        var current = Process.GetCurrentProcess();
        var parent = current.GetParentProcessId();

        Assert.NotNull(parent);
        Assert.NotEqual(current.Id, parent);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void GetParent()
    {
        var current = Process.GetCurrentProcess();
        var parent = current.GetParentProcess();
        var grandParent = parent.GetParentProcess();

        Assert.NotNull(grandParent);

        var descendants = grandParent.GetDescendantProcesses();
        Assert.Contains(descendants, p => p.Id == current.Id);
        Assert.Contains(descendants, p => p.Id == parent.Id);
    }
}
