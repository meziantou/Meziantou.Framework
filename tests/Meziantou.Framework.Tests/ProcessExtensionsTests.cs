#pragma warning disable MEZ_NETCORE3_1
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

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
    public Task RunAsTask_ProcessDoesNotExists()
    {
        var psi = new ProcessStartInfo("ProcessDoesNotExists.exe");

        return new Func<Task>(() => psi.RunAsTaskAsync(CancellationToken.None)).Should().ThrowExactlyAsync<Win32Exception>();
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

            await Task.Delay(100, cts.Token);

            (stopwatch.Elapsed > TimeSpan.FromSeconds(10)).Should().BeFalse("Cannot find the process");
        }

        await cts.CancelAsync();
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

            (processes.Count == 1 || processes.Count == 2).Should().BeTrue($"There should be 1 or 2 children (ping and conhost): {string.Join(',', processes.Select(p => p.ProcessName))}");
            processes.Any(p => p.ProcessName.EqualsIgnoreCase("PING") || p.ProcessName.EqualsIgnoreCase("CONHOST")).Should().BeTrue($"PING and CONHOST are not in the child processes: {string.Join(',', processes.Select(p => p.ProcessName))}");
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
}
