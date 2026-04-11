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
        var result = CreateEchoCommand("test")
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Equal(0, processResult.ExitCode);
        Assert.Single(processResult.Output.StandardOutput);
        Assert.Equal("test", processResult.Output.StandardOutput.First().Text);
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

        var result = command
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Single(processResult.Output.StandardError);
        Assert.Equal("error", processResult.Output.StandardError.First().Text.Trim());
    }

    [Fact]
    public async Task ExecuteBufferedAsync_ReturnsProcessId()
    {
        var result = CreateEchoCommand("test")
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.True(processResult.ProcessId > 0);
    }

    [Fact]
    public async Task ExecuteBufferedAsync_ReturnsTiming()
    {
        var result = CreateEchoCommand("test")
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.True(processResult.StartDate <= processResult.ExitDate);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_Action()
    {
        var lines = new List<string>();

        var process = CreateEchoCommand("test")
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

        var process = CreateEchoCommand("test")
            .WithOutputStream(sb)
            .ExecuteAsync();

        await process;

        Assert.Contains("test", sb.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_StringBuilder_PreservesOutputWithoutTrailingNewline()
    {
        var sb = new StringBuilder();

        var process = CreateNoNewlineOutputCommand("test")
            .WithOutputStream(sb)
            .ExecuteAsync();

        await process;

        Assert.Equal("test", sb.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputEncoding_UsesConfiguredEncoding()
    {
        var sb = new StringBuilder();

        var process = CreateSingleByteOutputCommand(0xE9)
            .WithOutputEncoding(Encoding.Latin1)
            .WithOutputStream(sb)
            .ExecuteAsync();

        await process;

        Assert.Equal("é", sb.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_ProcessOutputCollection()
    {
        var output = new ProcessOutputCollection();

        var process = CreateEchoCommand("test")
            .WithOutputStream(output)
            .ExecuteAsync();

        await process;

        Assert.Single(output.StandardOutput);
        Assert.Equal("test", output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_Stream()
    {
        using var output = new MemoryStream();
        var process = CreateEchoCommand("test")
            .WithOutputStream(output)
            .ExecuteAsync();

        await process;

        var capturedText = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("test", capturedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_Stream_AndTextHandler()
    {
        using var output = new MemoryStream();
        var lines = new List<string>();

        var process = CreateEchoCommand("test")
            .WithOutputStream(output)
            .WithOutputStream(line => { lock (lines) { lines.Add(line); } })
            .ExecuteAsync();

        await process;

        Assert.Single(lines);
        Assert.Equal("test", lines[0]);

        var capturedText = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("test", capturedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithOutputStream_Stream_DoesNotReplaceTextHandlers()
    {
        using var output = new MemoryStream();
        var lines = new List<string>();

        var process = CreateEchoCommand("test")
            .WithOutputStream(line => { lock (lines) { lines.Add(line); } })
            .WithOutputStream(output)
            .ExecuteAsync();

        await process;

        Assert.Single(lines);
        Assert.Equal("test", lines[0]);

        var capturedText = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("test", capturedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputStream_AccumulatesHandlers()
    {
        var list1 = new List<string>();
        var list2 = new List<string>();

        var process = CreateEchoCommand("test")
            .WithOutputStream(line => { lock (list1) { list1.Add(line); } })
            .WithOutputStream(line => { lock (list2) { list2.Add(line); } })
            .ExecuteAsync();

        await process;

        Assert.Single(list1);
        Assert.Single(list2);
        Assert.Equal("test", list1[0]);
        Assert.Equal("test", list2[0]);
    }

    [Fact]
    public async Task WithOutputStream_DoesNotReplaceExistingHandlers()
    {
        var list1 = new List<string>();
        var list2 = new List<string>();

        var process = CreateEchoCommand("test")
            .WithOutputStream(line => { lock (list1) { list1.Add(line); } })
            .WithOutputStream(line => { lock (list2) { list2.Add(line); } })
            .ExecuteAsync();

        await process;

        Assert.Single(list1);
        Assert.Single(list2);
    }

    [Fact]
    public async Task WithArguments_ReplacesArguments()
    {
        var result = CreateEchoCommand("first")
            .WithArguments(GetEchoArguments("second"))
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Equal("second", processResult.Output.StandardOutput.First().Text);
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

        var tempDir = NormalizeWorkingDirectoryPath(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));

        var result = command
            .WithWorkingDirectory(tempDir)
            .ExecuteBufferedAsync();

        var processResult = await result;

        var outputDir = NormalizeWorkingDirectoryPath(processResult.Output.StandardOutput.First().Text.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Equal(tempDir, outputDir, ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public async Task WithWorkingDirectory_FindsExecutableInWorkingDirectory()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var temporaryDirectoryPath = temporaryDirectory.FullPath.Value;

        string executableName;
        if (OperatingSystem.IsWindows())
        {
            executableName = "run";
            temporaryDirectory.CreateTextFile(executableName + ".bat", "@echo off\r\necho test-from-working-directory\r\n");
        }
        else
        {
            executableName = "run.sh";
            var scriptPath = temporaryDirectory.CreateTextFile(executableName, "#!/bin/sh\necho test-from-working-directory\n");
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var result = ProcessWrapper.Create(executableName)
            .WithWorkingDirectory(temporaryDirectoryPath)
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Equal("test-from-working-directory", processResult.Output.StandardOutput.First().Text.Trim());
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

        var result = command
            .WithEnvironmentVariables(env => env.Set("TEST_VAR_42", "hello"))
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Equal("hello", processResult.Output.StandardOutput.First().Text);
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

        var result = command
            .WithEnvironmentVariables(new Dictionary<string, string?>(StringComparer.Ordinal) { ["TEST_VAR_43"] = "world" })
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Equal("world", processResult.Output.StandardOutput.First().Text);
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

        var result = command
            .WithEnvironmentVariables(env => env.Set("TEST_VAR_44", "first"))
            .WithEnvironmentVariables(new Dictionary<string, string?>(StringComparer.Ordinal) { ["TEST_VAR_44"] = "second" })
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Equal("second", processResult.Output.StandardOutput.First().Text);
    }

    [Fact]
    public async Task Validation_FailIfNonZeroExitCode_Throws()
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

        var process = command.ExecuteAsync();

        var ex = await Assert.ThrowsAsync<ProcessExecutionException>(async () => await process);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task Validation_None_DoesNotThrow()
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

        var process = command
            .WithValidation(ProcessValidationMode.None)
            .ExecuteAsync();

        var processResult = await process;
        Assert.Equal(42, processResult.ExitCode);
    }

    [Fact]
    public async Task Validation_FailIfStdError_Throws()
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

        var process = command
            .WithValidation(ProcessValidationMode.FailIfStdError)
            .ExecuteAsync();

        var ex = await Assert.ThrowsAsync<ProcessExecutionException>(async () => await process);
        Assert.Equal("Process wrote to standard error.", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithErrorStream_Stream()
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

        using var error = new MemoryStream();
        var process = command
            .WithErrorStream(error)
            .WithValidation(ProcessValidationMode.None)
            .ExecuteAsync();

        await process;

        var capturedText = Encoding.UTF8.GetString(error.ToArray());
        Assert.Contains("error", capturedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WithErrorEncoding_UsesConfiguredEncoding()
    {
        var sb = new StringBuilder();
        var process = CreateSingleByteOutputCommand(0xE9, standardError: true)
            .WithValidation(ProcessValidationMode.None)
            .WithErrorEncoding(Encoding.Latin1)
            .WithErrorStream(sb)
            .ExecuteAsync();

        await process;

        Assert.Equal("é", sb.ToString());
    }

    [Fact]
    public async Task Validation_FailIfStdError_WithBinaryHandler_Throws()
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

        using var error = new MemoryStream();
        var process = command
            .WithErrorStream(error)
            .WithValidation(ProcessValidationMode.FailIfStdError)
            .ExecuteAsync();

        await Assert.ThrowsAsync<ProcessExecutionException>(async () => await process);
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

        var process = command
            .WithValidation(ProcessValidationMode.None)
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
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var process = ProcessWrapper.Create("NonExistentProcess_12345.exe")
                .ExecuteAsync();
            await process;
        });
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

        var result = command
            .WithInputStream("hello from stdin")
            .ExecuteBufferedAsync();

        var processResult = await result;

        Assert.Contains("hello from stdin", processResult.Output.StandardOutput.First().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReusableConfiguration()
    {
        var baseCommand = CreateEchoBase();

        var process1 = baseCommand
            .WithArguments(GetEchoArguments("first"))
            .ExecuteBufferedAsync();

        var processResult1 = await process1;

        var process2 = baseCommand
            .WithArguments(GetEchoArguments("second"))
            .ExecuteBufferedAsync();

        var processResult2 = await process2;

        Assert.Equal("first", processResult1.Output.StandardOutput.First().Text);
        Assert.Equal("second", processResult2.Output.StandardOutput.First().Text);
    }

    [Fact]
    public void FluentMethods_ReturnCurrentInstance()
    {
        var command = ProcessWrapper.Create("dotnet");
        using var stream = new MemoryStream();

        Assert.Same(command, command.WithArguments("--version"));
        Assert.Same(command, command.WithWorkingDirectory(Path.GetTempPath()));
        Assert.Same(command, command.WithEnvironmentVariables(env => env.Set("TEST_VAR_45", "value")));
        Assert.Same(command, command.WithEnvironmentVariables(new Dictionary<string, string?>(StringComparer.Ordinal) { ["TEST_VAR_45"] = "updated" }));
        Assert.Same(command, command.WithValidation(ProcessValidationMode.None));
        Assert.Same(command, command.WithOutputEncoding(Encoding.UTF8));
        Assert.Same(command, command.WithErrorEncoding(Encoding.UTF8));
        Assert.Same(command, command.WithOutputStream(_ => { }));
        Assert.Same(command, command.WithOutputStream(stream));
        Assert.Same(command, command.WithErrorStream(_ => { }));
        Assert.Same(command, command.WithErrorStream(stream));
        Assert.Same(command, command.WithInputStream("stdin"));
        Assert.Same(command, command.WithLimits(new ProcessLimits()));
        Assert.Same(command, command.WithLimits(limits => limits.CpuPercentage = 50));
        Assert.Same(command, command.WithWindowsJobObject(_ => { }));
        Assert.Same(command, command.WithLinuxControlGroup(_ => { }));
    }

    [Fact]
    public void ExecuteAsync_WithInvalidCpuLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = CreateEchoCommand("test")
                .WithLimits(new ProcessLimits { CpuPercentage = 0 })
                .ExecuteAsync();
        });
    }

    [Fact]
    public void ExecuteAsync_WithInvalidMemoryLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = CreateEchoCommand("test")
                .WithLimits(new ProcessLimits { MemoryLimitInBytes = -1 })
                .ExecuteAsync();
        });
    }

    [Fact]
    public void ExecuteAsync_WithInvalidProcessCountLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = CreateEchoCommand("test")
                .WithLimits(new ProcessLimits { ProcessCountLimit = 0 })
                .ExecuteAsync();
        });
    }

    [Fact]
    public void ExecuteAsync_WithUnsupportedPlatformSpecificConfiguration_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
            {
                _ = CreateEchoCommand("test")
                    .WithLinuxControlGroup(_ => { })
                    .ExecuteAsync();
            });

            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
            {
                _ = CreateEchoCommand("test")
                    .WithWindowsJobObject(_ => { })
                    .ExecuteAsync();
            });

            return;
        }

        Assert.Throws<PlatformNotSupportedException>(() =>
        {
            _ = CreateEchoCommand("test")
                .WithLimits(new ProcessLimits { CpuPercentage = 10 })
                .ExecuteAsync();
        });
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsProcessId()
    {
        var process = CreateEchoCommand("test")
            .ExecuteAsync();

        await process;

        Assert.True(process.ProcessId > 0);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentAwait_ReturnsSameResultInstance()
    {
        var process = CreateEchoCommand("test")
            .ExecuteAsync();

        using var gate = new ManualResetEventSlim(initialState: false);
        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () =>
            {
                gate.Wait();
                return await process;
            }))
            .ToArray();

        gate.Set();
        var results = await Task.WhenAll(tasks);

        var firstResult = results[0];
        Assert.All(results, result => Assert.Same(firstResult, result));
    }

    [Fact]
    public async Task ExecuteAsync_ReleasesHandleAfterExitWithoutAwait()
    {
        var process = CreateEchoCommand("test")
            .ExecuteAsync();

        var stopwatch = Stopwatch.StartNew();
        while (process.UnsafeGetProcessHandle() is not null)
        {
            await Task.Delay(50);
            Assert.False(stopwatch.Elapsed > TimeSpan.FromSeconds(10));
        }

        Assert.Null(process.UnsafeGetProcessHandle());
    }

    [Fact]
    public async Task ExecuteAsync_Kill()
    {
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

        var process = command
            .WithValidation(ProcessValidationMode.None)
            .ExecuteAsync();

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var processes = Process.GetProcesses();
            if (processes.Any(p => p.ProcessName.Contains("ping", StringComparison.OrdinalIgnoreCase)))
                break;

            await Task.Delay(100);
            Assert.False(stopwatch.Elapsed > TimeSpan.FromSeconds(10));
        }

        process.Kill(entireProcessTree: false);
        process.Kill();

        var processResult = await process;
        Assert.True(processResult.ProcessId > 0);
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

    private static ProcessWrapper CreateNoNewlineOutputCommand(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProcessWrapper.Create("cmd.exe")
                .WithArguments("/C", $"set /p ={text}<nul&exit /b 0");
        }

        return ProcessWrapper.Create("sh")
            .WithArguments("-c", $"printf '%s' \"{text}\"");
    }

    private static ProcessWrapper CreateSingleByteOutputCommand(byte value, bool standardError = false)
    {
        if (OperatingSystem.IsWindows())
        {
            var streamAccessor = standardError ? "StandardError" : "StandardOutput";
            return ProcessWrapper.Create("powershell.exe")
                .WithArguments(
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    $"[Console]::Open{streamAccessor}().Write([byte[]]({value}), 0, 1)");
        }

        var octalValue = Convert.ToString(value, 8).PadLeft(3, '0');
        var redirection = standardError ? " >&2" : "";
        return ProcessWrapper.Create("sh")
            .WithArguments("-c", $"printf '\\{octalValue}'{redirection}");
    }

    private static string[] GetEchoArguments(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            return ["/C", $"echo {text}"];
        }

        return [text];
    }

    private static string NormalizeWorkingDirectoryPath(string path)
    {
        if (!OperatingSystem.IsWindows() && path.StartsWith("/private/", StringComparison.Ordinal))
        {
            return path["/private".Length..];
        }

        return path;
    }
}
