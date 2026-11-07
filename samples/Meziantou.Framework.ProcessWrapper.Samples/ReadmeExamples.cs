using System.Runtime.Versioning;
using System.Text;
using Meziantou.Framework.Win32;

namespace Meziantou.Framework.Samples;

// The examples of src/Meziantou.Framework.ProcessWrapper/readme.md that start placeholder commands (my-app,
// process-a...), so they are compiled but not run. The examples that can run are in the tests of ProcessWrapper.
public static class ReadmeExamples
{
    public static async Task WorkingDirectory()
    {
        #region ProcessWrapper_WorkingDirectory
        await ProcessWrapper.Create("git")
            .WithArguments("status")
            .WithWorkingDirectory("/path/to/repo")
            .ExecuteAsync();
        #endregion
    }

    public static async Task SearchWorkingDirectory()
    {
        #region ProcessWrapper_SearchWorkingDirectory
        await ProcessWrapper.Create("run.sh")
            .WithWorkingDirectory("/path/to/scripts")
            .WithSearchWorkingDirectory()
            .ExecuteAsync();
        #endregion
    }

    public static async Task EnvironmentVariables()
    {
        #region ProcessWrapper_EnvironmentVariables
        // Using callback
        await ProcessWrapper.Create("my-app")
            .WithEnvironmentVariables(env => env
                .Set("MY_VAR", "value")
                .Remove("UNWANTED_VAR"))
            .ExecuteAsync();

        // Using dictionary (null removes the variable)
        await ProcessWrapper.Create("my-app")
            .WithEnvironmentVariables(new Dictionary<string, string?>
            {
                ["MY_VAR"] = "value",
                ["UNWANTED_VAR"] = null,
            })
            .ExecuteAsync();
        #endregion
    }

    public static async Task<ProcessResult> ResourceLimits()
    {
        #region ProcessWrapper_ResourceLimits
        var result = await ProcessWrapper.Create("my-app")
            .WithLimits(new ProcessLimits
            {
                CpuPercentage = 50,            // 50% max CPU
                MemoryLimitInBytes = 512L * 1024 * 1024, // 512 MB
                ProcessCountLimit = 20,
            })
            .ExecuteAsync();
        #endregion

        return result;
    }

    [SupportedOSPlatform("windows5.1.2600")]
    public static async Task<ProcessResult> WindowsJobObject()
    {
        #region ProcessWrapper_WindowsJobObject
        var result = await ProcessWrapper.Create("my-app")
            .WithLimits(limits => limits.MemoryLimitInBytes = 512L * 1024 * 1024)
            .WithWindowsJobObject(job =>
            {
                job.SetLimits(new JobObjectLimits
                {
                    Flags = JobObjectLimitFlags.KillOnJobClose,
                });
            })
            .ExecuteAsync();
        #endregion

        return result;
    }

    [SupportedOSPlatform("linux")]
    public static async Task<ProcessResult> LinuxControlGroup()
    {
        #region ProcessWrapper_LinuxControlGroup
        var result = await ProcessWrapper.Create("my-app")
            .WithLinuxControlGroup(cgroup =>
            {
                cgroup.SetMemoryHigh(256L * 1024 * 1024);
            })
            .ExecuteAsync();
        #endregion

        return result;
    }

    public static async Task OutputHandling()
    {
        #region ProcessWrapper_OutputHandling
        // Stream output line by line
        await ProcessWrapper.Create("dotnet")
            .WithArguments("build")
            .WithOutputStream(OutputTarget.ToTextDelegate(line => Console.WriteLine($"[OUT] {line}")))
            .WithErrorStream(OutputTarget.ToTextDelegate(line => Console.Error.WriteLine($"[ERR] {line}")))
            .ExecuteAsync();

        // Collect output into a StringBuilder
        var sb = new StringBuilder();
        await ProcessWrapper.Create("dotnet")
            .WithArguments("build")
            .WithOutputStream(sb)
            .ExecuteAsync();

        Console.WriteLine(sb.ToString());

        // Stream lines to a TextWriter
        using var writer = new StringWriter();
        await ProcessWrapper.Create("dotnet")
            .WithArguments("--version")
            .WithOutputStream(OutputTarget.ToTextWriter(writer))
            .ExecuteAsync();

        // Collect into a ProcessOutputCollection
        var output = new ProcessOutputCollection();
        await ProcessWrapper.Create("dotnet")
            .WithArguments("build")
            .WithOutputStream(output)
            .WithErrorStream(output)
            .ExecuteAsync();

        foreach (var line in output.StandardError)
        {
            Console.Error.WriteLine($"[{line.TimestampUtc:O}] {line.Text}");
        }

        // Capture raw bytes from stdout/stderr
        await using var stdout = File.Create("stdout.bin");
        await using var stderr = File.Create("stderr.bin");

        await ProcessWrapper.Create("my-command")
            .WithOutputStream(stdout)
            .WithErrorStream(stderr)
            .ExecuteAsync();

        // Text and binary handlers can be combined on the same stream
        await using var rawOutput = File.Create("raw-output.bin");
        await ProcessWrapper.Create("dotnet")
            .WithArguments("--version")
            .WithOutputStream(rawOutput)
            .AddOutputStream(OutputTarget.ToTextDelegate(line => Console.WriteLine($"Version line: {line}")))
            .ExecuteAsync();

        // Capture raw bytes using a delegate
        await ProcessWrapper.Create("dotnet")
            .WithArguments("--version")
            .WithOutputStream(OutputTarget.ToBytesDelegate(bytes => Console.WriteLine($"Chunk size: {bytes.Length}")))
            .ExecuteAsync();

        // Override stdout/stderr decoding when process output is not UTF-8
        await ProcessWrapper.Create("my-command")
            .WithOutputEncoding(Encoding.Latin1)
            .WithErrorEncoding(Encoding.Latin1)
            .WithOutputStream(OutputTarget.ToTextDelegate(line => Console.WriteLine(line)))
            .WithErrorStream(OutputTarget.ToTextDelegate(line => Console.Error.WriteLine(line)))
            .ExecuteAsync();
        #endregion
    }

    public static async Task InputStream()
    {
        #region ProcessWrapper_InputStream
        // Pipe a string to stdin
        var result = await ProcessWrapper.Create("cat")
            .WithInputStream(InputSource.FromText("Hello, World!"))
            .ExecuteBufferedAsync();

        Console.WriteLine(result.Output.ToString());

        // Pipe a file to stdin
        var fileResult = await ProcessWrapper.Create("cat")
            .WithInputStream(InputSource.FromFile("input.txt"))
            .ExecuteBufferedAsync();

        Console.WriteLine(fileResult.Output.ToString());

        // Pipe from a TextReader
        using var reader = new StringReader("Hello from reader");
        var readerResult = await ProcessWrapper.Create("cat")
            .WithInputStream(InputSource.FromTextReader(reader))
            .ExecuteBufferedAsync();

        Console.WriteLine(readerResult.Output.ToString());

        // Pipe one process output to another process input
        var pipe = new ProcessPipe(maxBufferSize: 256 * 1024);

        var downstream = ProcessWrapper.Create("process-b")
            .WithInputStream(pipe)
            .ExecuteAsync();

        await ProcessWrapper.Create("process-a")
            .WithArguments("--generate-data")
            .AddOutputStream(pipe)
            .ExecuteAsync();

        await downstream;
        #endregion
    }

    public static async Task PipeOperator()
    {
        #region ProcessWrapper_PipeOperator
        var result = await (ProcessWrapper.Create("process-a")
            .WithArguments("--generate-data")
            | ProcessWrapper.Create("process-b")
            | ProcessWrapper.Create("process-c"))
            .ExecuteBufferedAsync();

        // Buffered output corresponds to the last command (process-c)
        foreach (var line in result.Output.StandardOutput)
        {
            Console.WriteLine(line.Text);
        }
        #endregion
    }

    public static async Task<(int ExitCode, bool IsSuccess)> Validation()
    {
        #region ProcessWrapper_Validation
        // Default: throws ProcessExecutionException if exit code is non-zero
        try
        {
            await ProcessWrapper.Create("false")
                .ExecuteAsync();
        }
        catch (ProcessExecutionException ex)
        {
            Console.WriteLine($"Process failed with exit code {ex.ExitCode}");
        }

        // Disable validation
        var result = await ProcessWrapper.Create("false")
            .WithValidation(ProcessValidationMode.None)
            .ExecuteAsync();
        int exitCode = result.ExitCode;
        bool isSuccess = result.ExitCode.IsSuccess;

        // Fail on stderr output as well
        await ProcessWrapper.Create("my-command")
            .WithValidation(ProcessValidationMode.FailIfNonZeroExitCode | ProcessValidationMode.FailIfStdError)
            .ExecuteAsync();

        // Control command details included in exception messages and ProcessResult.ToString()
        ProcessWrapper.DefaultLogVerbosity = ProcessLogVerbosity.IncludeProcessPath;

        await ProcessWrapper.Create("my-command")
            .WithArguments("--token", "secret")
            .WithLogVerbosity(ProcessLogVerbosity.IncludeProcessPath | ProcessLogVerbosity.IncludeArguments)
            .ExecuteAsync();
        #endregion

        return (exitCode, isSuccess);
    }

    public static async Task Cancellation()
    {
        #region ProcessWrapper_Cancellation
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ProcessWrapper.Create("long-running-process")
            .ExecuteAsync(cts.Token);  // throws OperationCanceledException if cancelled
        #endregion
    }

    public static async Task Kill()
    {
        #region ProcessWrapper_Kill
        var process = ProcessWrapper.Create("long-running-process")
            .WithValidation(ProcessValidationMode.None)
            .ExecuteAsync();

        process.Kill(); // or process.Kill(entireProcessTree: false)
        await process;
        #endregion
    }
}
