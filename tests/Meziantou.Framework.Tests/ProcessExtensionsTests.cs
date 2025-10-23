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
        static Task<ProcessResult> CreateProcess()
        {
            if (OperatingSystem.IsWindows())
                return ProcessExtensions.RunAsTaskAsync("cmd", "/C echo test", CancellationToken.None);

            return ProcessExtensions.RunAsTaskAsync("echo", "test", CancellationToken.None);
        }

        var result = await CreateProcess();
        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Output);
        Assert.Equal("test", result.Output[0].Text);
        Assert.Equal(ProcessOutputType.StandardOutput, result.Output[0].Type);
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

        var result = await psi.RunAsTaskAsync(redirectOutput: true, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Output);
        Assert.Equal("test", result.Output[0].Text);
        Assert.Equal(ProcessOutputType.StandardOutput, result.Output[0].Type);
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

        var result = await psi.RunAsTaskAsync(redirectOutput: false, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task RunAsTask_ProcessDoesNotExists()
    {
        var psi = new ProcessStartInfo("ProcessDoesNotExists.exe");
        await Assert.ThrowsAsync<Win32Exception>(() => psi.RunAsTaskAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunAsTask_Cancel()
    {
        var stopwatch = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource();
        Task task;
        if (OperatingSystem.IsWindows())
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

            await Task.Delay(100, cts.Token);
            Assert.False(stopwatch.Elapsed > TimeSpan.FromSeconds(10));
        }

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
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
