#nullable enable
using System.Diagnostics;
using System.Text;

namespace Meziantou.Framework.Tests;

[Collection("ProcessWrapper")]
public class ProcessWrapperTests
{
    [Fact]
    public async Task ExecuteBufferedAsync_CapturesOutput()
    {
        using var result = CreateEchoCommand("test")
            .ExecuteBufferedAsync();

        var exitCode = await result;

        Assert.Equal(0, exitCode);
        Assert.Single(result.Output.StandardOutput);
        Assert.Equal("test", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task ExecuteBufferedAsync_CapturesStdErr()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", "echo error>&2");
        }
        else
        {
            command = ProcessWrapper.Create("sh")
                .WithArguments("-c", "echo error >&2");
        }

        using var result = command
            .WithExitCodeValidation(ExitCodeValidationMode.None)
            .ExecuteBufferedAsync();

        await result;

        Assert.Single(result.Output.StandardError);
        Assert.Equal("error", result.Output.StandardError.First().Text.Trim());
    }

    [Fact]
    public async Task ExecuteBufferedAsync_ReturnsProcessId()
    {
        using var result = CreateEchoCommand("test")
            .ExecuteBufferedAsync();

        await result;

        Assert.True(result.ProcessId > 0);
    }

    [Fact]
    public async Task ExecuteBufferedAsync_ReturnsTiming()
    {
        using var result = CreateEchoCommand("test")
            .ExecuteBufferedAsync();

        await result;

        Assert.True(result.StartTime <= result.EndTime);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_Action()
    {
        var lines = new List<string>();

        using var process = CreateEchoCommand("test")
            .WithOutputStream(line => { lock (lines) { lines.Add(line); } })
            .ExecuteAsync();

        await process;

        Assert.Single(lines);
        Assert.Equal("test", lines[0]);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_StringBuilder()
    {
        var sb = new StringBuilder();

        using var process = CreateEchoCommand("test")
            .WithOutputStream(sb)
            .ExecuteAsync();

        await process;

        Assert.Contains("test", sb.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_ProcessOutputCollection()
    {
        var output = new ProcessOutputCollection();

        using var process = CreateEchoCommand("test")
            .WithOutputStream(output)
            .ExecuteAsync();

        await process;

        Assert.Single(output.StandardOutput);
        Assert.Equal("test", output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task ExecuteAsync_AddOutputStream_AccumulatesHandlers()
    {
        var list1 = new List<string>();
        var list2 = new List<string>();

        using var process = CreateEchoCommand("test")
            .AddOutputStream(line => { lock (list1) { list1.Add(line); } })
            .AddOutputStream(line => { lock (list2) { list2.Add(line); } })
            .ExecuteAsync();

        await process;

        Assert.Single(list1);
        Assert.Single(list2);
        Assert.Equal("test", list1[0]);
        Assert.Equal("test", list2[0]);
    }

    [Fact]
    public async Task WithOutputStream_ReplacesAllHandlers()
    {
        var list1 = new List<string>();
        var list2 = new List<string>();

        using var process = CreateEchoCommand("test")
            .AddOutputStream(line => { lock (list1) { list1.Add(line); } })
            .WithOutputStream(line => { lock (list2) { list2.Add(line); } })
            .ExecuteAsync();

        await process;

        Assert.Empty(list1);
        Assert.Single(list2);
    }

    [Fact]
    public async Task WithArguments_ReplacesArguments()
    {
        using var result = CreateEchoCommand("first")
            .WithArguments(GetEchoArguments("second"))
            .ExecuteBufferedAsync();

        await result;

        Assert.Equal("second", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task WithWorkingDirectory_SetsWorkingDirectory()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", "cd");
        }
        else
        {
            command = ProcessWrapper.Create("pwd");
        }

        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        using var result = command
            .WithWorkingDirectory(tempDir)
            .ExecuteBufferedAsync();

        await result;

        var outputDir = result.Output.StandardOutput.First().Text.TrimEnd(Path.DirectorySeparatorChar);
        Assert.Equal(tempDir, outputDir, ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public async Task WithEnvironmentVariables_Callback_SetsVariable()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", "echo %TEST_VAR_42%");
        }
        else
        {
            command = ProcessWrapper.Create("sh")
                .WithArguments("-c", "echo $TEST_VAR_42");
        }

        using var result = command
            .WithEnvironmentVariables(env => env.Set("TEST_VAR_42", "hello"))
            .ExecuteBufferedAsync();

        await result;

        Assert.Equal("hello", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task WithEnvironmentVariables_Dictionary_SetsVariable()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", "echo %TEST_VAR_43%");
        }
        else
        {
            command = ProcessWrapper.Create("sh")
                .WithArguments("-c", "echo $TEST_VAR_43");
        }

        using var result = command
            .WithEnvironmentVariables(new Dictionary<string, string?> { ["TEST_VAR_43"] = "world" })
            .ExecuteBufferedAsync();

        await result;

        Assert.Equal("world", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task WithEnvironmentVariables_LastValueWins()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", "echo %TEST_VAR_44%");
        }
        else
        {
            command = ProcessWrapper.Create("sh")
                .WithArguments("-c", "echo $TEST_VAR_44");
        }

        using var result = command
            .WithEnvironmentVariables(env => env.Set("TEST_VAR_44", "first"))
            .WithEnvironmentVariables(new Dictionary<string, string?> { ["TEST_VAR_44"] = "second" })
            .ExecuteBufferedAsync();

        await result;

        Assert.Equal("second", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task ExitCodeValidation_FailIfNotZero_Throws()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", "exit 1");
        }
        else
        {
            command = ProcessWrapper.Create("sh")
                .WithArguments("-c", "exit 1");
        }

        using var process = command.ExecuteAsync();

        var ex = await Assert.ThrowsAsync<ProcessExecutionException>(async () => await process);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task ExitCodeValidation_None_DoesNotThrow()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", "exit 42");
        }
        else
        {
            command = ProcessWrapper.Create("sh")
                .WithArguments("-c", "exit 42");
        }

        using var process = command
            .WithExitCodeValidation(ExitCodeValidationMode.None)
            .ExecuteAsync();

        var exitCode = await process;
        Assert.Equal(42, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Cancel()
    {
        using var cts = new CancellationTokenSource();

        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("ping.exe")
                .WithArguments("127.0.0.1", "-n", "100");
        }
        else
        {
            command = ProcessWrapper.Create("ping")
                .WithArguments("127.0.0.1", "-c", "100");
        }

        using var process = command
            .WithExitCodeValidation(ExitCodeValidationMode.None)
            .ExecuteAsync(cts.Token);

        // Wait for the process to start
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var processes = Process.GetProcesses();
            if (processes.Any(p => p.ProcessName.Contains("ping", StringComparison.OrdinalIgnoreCase)))
                break;

            await Task.Delay(100, cts.Token);
            Assert.False(stopwatch.Elapsed > TimeSpan.FromSeconds(10));
        }

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await process);
    }

    [Fact]
    public async Task ProcessDoesNotExist_Throws()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            ProcessWrapper.Create("NonExistentProcess_12345.exe")
                .ExecuteAsync();
        });

        await Task.CompletedTask;
    }

    [Fact]
    public async Task WithInputStream_String()
    {
        ProcessWrapper command;
        if (OperatingSystem.IsWindows())
        {
            command = ProcessWrapper.Create("findstr")
                .WithArguments(".*");
        }
        else
        {
            command = ProcessWrapper.Create("cat");
        }

        using var result = command
            .WithInputStream("hello from stdin")
            .ExecuteBufferedAsync();

        await result;

        Assert.Contains("hello from stdin", result.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task ReusableConfiguration()
    {
        var baseCommand = CreateEchoBase();

        using var process1 = baseCommand
            .WithArguments(GetEchoArguments("first"))
            .ExecuteBufferedAsync();

        await process1;

        using var process2 = baseCommand
            .WithArguments(GetEchoArguments("second"))
            .ExecuteBufferedAsync();

        await process2;

        Assert.Equal("first", process1.Output.StandardOutput.First().Text);
        Assert.Equal("second", process2.Output.StandardOutput.First().Text);
    }

    [Fact]
    public void FluentMethods_ReturnCurrentInstance()
    {
        var command = ProcessWrapper.Create("dotnet");

        Assert.Same(command, command.WithArguments("--version"));
        Assert.Same(command, command.WithWorkingDirectory(Path.GetTempPath()));
        Assert.Same(command, command.WithEnvironmentVariables(env => env.Set("TEST_VAR_45", "value")));
        Assert.Same(command, command.WithEnvironmentVariables(new Dictionary<string, string?> { ["TEST_VAR_45"] = "updated" }));
        Assert.Same(command, command.WithExitCodeValidation(ExitCodeValidationMode.None));
        Assert.Same(command, command.WithOutputStream(_ => { }));
        Assert.Same(command, command.AddOutputStream(_ => { }));
        Assert.Same(command, command.WithErrorStream(_ => { }));
        Assert.Same(command, command.AddErrorStream(_ => { }));
        Assert.Same(command, command.WithInputStream("stdin"));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsProcessId()
    {
        using var process = CreateEchoCommand("test")
            .ExecuteAsync();

        await process;

        Assert.True(process.ProcessId > 0);
    }

    private static ProcessWrapper CreateEchoBase()
    {
        if (OperatingSystem.IsWindows())
        {
            return ProcessWrapper.Create("cmd.exe");
        }

        return ProcessWrapper.Create("echo");
    }

    private static ProcessWrapper CreateEchoCommand(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", $"echo {text}");
        }

        return ProcessWrapper.Create("echo")
            .WithArguments(text);
    }

    private static string[] GetEchoArguments(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            return ["/C", $"echo {text}"];
        }

        return [text];
    }
}
