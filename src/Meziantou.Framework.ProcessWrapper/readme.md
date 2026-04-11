# Meziantou.Framework.ProcessWrapper

Fluent, immutable API for configuring and running processes.

## Basic usage

````c#
// Execute and wait for exit (throws if exit code is non-zero by default)
var result = await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .ExecuteAsync();

int exitCode = result.ExitCode;
````

## Buffered execution

````c#
// Capture all output
var result = await ProcessWrapper.Create("dotnet")
    .WithArguments("--info")
    .ExecuteBufferedAsync();

int exitCode = result.ExitCode;

// Access output after awaiting
foreach (var line in result.Output.StandardOutput)
{
    Console.WriteLine(line.Text);
}
````

## Working directory

````c#
await ProcessWrapper.Create("git")
    .WithArguments("status")
    .WithWorkingDirectory("/path/to/repo")
    .ExecuteAsync();
````

## Environment variables

````c#
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
````

## Resource limits

````c#
var result = await ProcessWrapper.Create("my-app")
    .WithLimits(new ProcessLimits
    {
        CpuPercentage = 50,            // 50% max CPU
        MemoryLimitInBytes = 512L * 1024 * 1024, // 512 MB
        ProcessCountLimit = 20,
    })
    .ExecuteAsync();
````

Common limits are mapped to Windows Job Objects or Linux cgroups v2 depending on the current OS.
If a configured limit cannot be applied on the current platform, execution throws.

### Advanced platform-specific configuration

````c#
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
````

````c#
var result = await ProcessWrapper.Create("my-app")
    .WithLinuxControlGroup(cgroup =>
    {
        cgroup.SetMemoryHigh(256L * 1024 * 1024);
    })
    .ExecuteAsync();
````

## Output handling

Use `With*` methods to replace handlers, and `Add*` methods to append additional handlers.

````c#
// Stream output line by line
await ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .WithOutputStream(line => Console.WriteLine($"[OUT] {line}"))
    .WithErrorStream(line => Console.Error.WriteLine($"[ERR] {line}"))
    .ExecuteAsync();

// Collect output into a StringBuilder
var sb = new StringBuilder();
await ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .WithOutputStream(sb)
    .ExecuteAsync();

Console.WriteLine(sb.ToString());

// Collect into a ProcessOutputCollection
var output = new ProcessOutputCollection();
await ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .WithOutputStream(output)
    .WithErrorStream(output)
    .ExecuteAsync();

foreach (var line in output.StandardError)
{
    Console.Error.WriteLine(line.Text);
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
    .AddOutputStream(line => Console.WriteLine($"Version line: {line}"))
    .ExecuteAsync();

// Override stdout/stderr decoding when process output is not UTF-8
await ProcessWrapper.Create("my-command")
    .WithOutputEncoding(Encoding.Latin1)
    .WithErrorEncoding(Encoding.Latin1)
    .WithOutputStream(line => Console.WriteLine(line))
    .WithErrorStream(line => Console.Error.WriteLine(line))
    .ExecuteAsync();
````

## Input stream

````c#
// Pipe a string to stdin
var result = await ProcessWrapper.Create("cat")
    .WithInputStream("Hello, World!")
    .ExecuteBufferedAsync();

Console.WriteLine(result.Output.ToString());
````

## Validation

````c#
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

// Fail on stderr output as well
await ProcessWrapper.Create("my-command")
    .WithValidation(ProcessValidationMode.FailIfNonZeroExitCode | ProcessValidationMode.FailIfStdError)
    .ExecuteAsync();
````

## Cancellation

````c#
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await ProcessWrapper.Create("long-running-process")
    .ExecuteAsync(cts.Token);  // throws OperationCanceledException if cancelled
````

## Killing a process

````c#
var process = ProcessWrapper.Create("long-running-process")
    .WithValidation(ProcessValidationMode.None)
    .ExecuteAsync();

process.Kill(); // or process.Kill(entireProcessTree: false)
await process;
````
