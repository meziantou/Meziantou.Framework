# Meziantou.Framework.ProcessWrapper

Fluent, immutable API for configuring and running processes.

## Basic usage

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_BasicUsage;format=md-fenced-code-block;dedent=true -->
```csharp
// Execute and wait for exit (throws if exit code is non-zero by default)
var result = await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .ExecuteAsync();

Assert.True(result.ExitCode.IsSuccess);
```
<!-- endref -->

## Buffered execution

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_BufferedExecution;format=md-fenced-code-block;dedent=true -->
```csharp
// Capture all output
var result = await ProcessWrapper.Create("dotnet")
    .WithArguments("--info")
    .ExecuteBufferedAsync();

int exitCode = result.ExitCode;
Assert.Equal(0, exitCode);

// Access output after awaiting
Assert.Contains(result.Output.StandardOutput, line => line.Text.StartsWith(".NET SDK", StringComparison.Ordinal));
```
<!-- endref -->

## Intercepting process execution (for tests)

Use `ProcessWrapper.DefaultProcessFactory` to intercept process creation globally.

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_DefaultProcessFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var previousFactory = ProcessWrapper.DefaultProcessFactory;
ProcessWrapper.DefaultProcessFactory = new FakeProcessFactory(startInfo =>
{
    return FakeProcess.Create(
        exitCode: 0,
        outputText: "simulated output",
        errorText: "");
});

try
{
    var result = await ProcessWrapper.Create("my-command")
        .WithArguments("--version")
        .ExecuteBufferedAsync();

    Assert.Equal("simulated output", result.Output.StandardOutput.Single().Text);
}
finally
{
    ProcessWrapper.DefaultProcessFactory = previousFactory;
}
```
<!-- endref -->

You can also override the factory per command:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_ProcessFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-command")
    .WithProcessFactory(new FakeProcessFactory(_ => FakeProcess.Create(0, "instance output", "")))
    .ExecuteBufferedAsync();

Assert.Equal("instance output", result.Output.StandardOutput.Single().Text);
```
<!-- endref -->

## Interceptors

Use interceptors to update commands globally or per instance before execution (`ProcessStartInfo` is in the
`System.Diagnostics` namespace).

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_InterceptorTypes;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class SampleProcessWrapperInterceptor : IProcessWrapperInterceptor
{
    public void Intercept(ProcessWrapper processWrapper)
    {
        // You can inspect/mutate the wrapper instance
        processWrapper.WithEnvironmentVariables(env => env.Set("TRACE_ID", Guid.NewGuid().ToString("N")));
    }
}

public sealed class SampleProcessStartInfoInterceptor : IProcessStartInfoInterceptor
{
    public void Intercept(ProcessWrapper processWrapper, ProcessStartInfo processStartInfo)
    {
        // You can inspect/mutate ProcessStartInfo right before process creation
        Console.WriteLine($"Executing: {processStartInfo.FileName}");
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_Interceptors;format=md-fenced-code-block;dedent=true -->
```csharp
using var globalWrapperRegistration = ProcessWrapper.AddInterceptor(new SampleProcessWrapperInterceptor());
using var globalStartInfoRegistration = ProcessWrapper.AddInterceptor(new SampleProcessStartInfoInterceptor());

var result = await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .WithInterceptor(new SampleProcessWrapperInterceptor())
    .WithInterceptor(new SampleProcessStartInfoInterceptor())
    .ExecuteBufferedAsync();
```
<!-- endref -->

## Working directory

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_WorkingDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
await ProcessWrapper.Create("git")
    .WithArguments("status")
    .WithWorkingDirectory("/path/to/repo")
    .ExecuteAsync();
```
<!-- endref -->

The executable is resolved from `PATH`. The working directory is not searched, so a file sitting in it cannot
shadow the command `PATH` resolves for the same name. Windows searches the current directory before `PATH` by
default, but that is disabled here too, for the same reason. Opt in with `WithSearchWorkingDirectory()` when the
working directory is trusted and you want the executable looked up there first:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_SearchWorkingDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
await ProcessWrapper.Create("run.sh")
    .WithWorkingDirectory("/path/to/scripts")
    .WithSearchWorkingDirectory()
    .ExecuteAsync();
```
<!-- endref -->

## Environment variables

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_EnvironmentVariables;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Resource limits

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_ResourceLimits;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-app")
    .WithLimits(new ProcessLimits
    {
        CpuPercentage = 50,            // 50% max CPU
        MemoryLimitInBytes = 512L * 1024 * 1024, // 512 MB
        ProcessCountLimit = 20,
    })
    .ExecuteAsync();
```
<!-- endref -->

Common limits are mapped to Windows Job Objects or Linux cgroups v2 depending on the current OS.
If a configured limit cannot be applied on the current platform, execution throws.

### Advanced platform-specific configuration

`WithWindowsJobObject` configures a `JobObject` (`JobObjectLimits` is in the `Meziantou.Framework.Win32` namespace),
whose methods are only supported on Windows:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_WindowsJobObject;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

`WithLinuxControlGroup` configures a `CGroup2` (in the `Meziantou.Framework.Unix.ControlGroups` namespace), whose
methods are only supported on Linux:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_LinuxControlGroup;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-app")
    .WithLinuxControlGroup(cgroup =>
    {
        cgroup.SetMemoryHigh(256L * 1024 * 1024);
    })
    .ExecuteAsync();
```
<!-- endref -->

## Output handling

Use `With*` methods to replace handlers, and `Add*` methods to append additional handlers (`StringBuilder` and
`Encoding` are in the `System.Text` namespace).

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_OutputHandling;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Input stream

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_InputStream;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

### Pipe operator (`|`)

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_PipeOperator;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The `|` operator wires the standard output of each command to the standard input of the next command.
Validation runs on every command in the pipeline, and execution fails if any command fails.

## Validation

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_Validation;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Cancellation

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_Cancellation;format=md-fenced-code-block;dedent=true -->
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await ProcessWrapper.Create("long-running-process")
    .ExecuteAsync(cts.Token);  // throws OperationCanceledException if cancelled
```
<!-- endref -->

## Killing a process

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_Kill;format=md-fenced-code-block;dedent=true -->
```csharp
var process = ProcessWrapper.Create("long-running-process")
    .WithValidation(ProcessValidationMode.None)
    .ExecuteAsync();

process.Kill(); // or process.Kill(entireProcessTree: false)
await process;
```
<!-- endref -->
