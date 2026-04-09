# Meziantou.Framework.ProcessWrapper

Fluent, immutable API for configuring and running processes. Inspired by CliWrap.

## Basic usage

````c#
// Execute and wait for exit (throws if exit code is non-zero by default)
var process = ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .ExecuteAsync();

var result = await process;
int exitCode = result.ExitCode;
````

## Buffered execution

````c#
// Capture all output
var result = ProcessWrapper.Create("dotnet")
    .WithArguments("--info")
    .ExecuteBufferedAsync();

var completedProcess = await result;
int exitCode = completedProcess.ExitCode;

// Access output after awaiting
foreach (var line in completedProcess.Output.StandardOutput)
{
    Console.WriteLine(line.Text);
}
````

## Working directory

````c#
var process = ProcessWrapper.Create("git")
    .WithArguments("status")
    .WithWorkingDirectory("/path/to/repo")
    .ExecuteAsync();

await process;
````

## Environment variables

````c#
// Using callback
var process = ProcessWrapper.Create("my-app")
    .WithEnvironmentVariables(env => env
        .Set("MY_VAR", "value")
        .Remove("UNWANTED_VAR"))
    .ExecuteAsync();

await process;

// Using dictionary (null removes the variable)
var process2 = ProcessWrapper.Create("my-app")
    .WithEnvironmentVariables(new Dictionary<string, string?>
    {
        ["MY_VAR"] = "value",
        ["UNWANTED_VAR"] = null,
    })
    .ExecuteAsync();

await process2;
````

## Output handling

````c#
// Stream output line by line
var process = ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .AddOutputStream(line => Console.WriteLine($"[OUT] {line}"))
    .AddErrorStream(line => Console.Error.WriteLine($"[ERR] {line}"))
    .ExecuteAsync();

await process;

// Collect output into a StringBuilder
var sb = new StringBuilder();
var process2 = ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .WithOutputStream(sb)
    .ExecuteAsync();

await process2;
Console.WriteLine(sb.ToString());

// Collect into a ProcessOutputCollection
var output = new ProcessOutputCollection();
var process3 = ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .AddOutputStream(output)
    .AddErrorStream(output)
    .ExecuteAsync();

await process3;

foreach (var line in output.StandardError)
{
    Console.Error.WriteLine(line.Text);
}
````

## Input stream

````c#
// Pipe a string to stdin
var process = ProcessWrapper.Create("cat")
    .WithInputStream("Hello, World!")
    .ExecuteBufferedAsync();

var result = await process;
Console.WriteLine(result.Output.ToString());
````

## Validation

````c#
// Default: throws ProcessExecutionException if exit code is non-zero
var process = ProcessWrapper.Create("false")
    .ExecuteAsync();

try
{
    await process; // throws ProcessExecutionException
}
catch (ProcessExecutionException ex)
{
    Console.WriteLine($"Process failed with exit code {ex.ExitCode}");
}

// Disable validation
var process2 = ProcessWrapper.Create("false")
    .WithValidation(ProcessValidationMode.None)
    .ExecuteAsync();

var result = await process2; // does not throw
int exitCode = result.ExitCode;

// Fail on stderr output as well
var process3 = ProcessWrapper.Create("my-command")
    .WithValidation(ProcessValidationMode.FailIfNonZeroExitCode | ProcessValidationMode.FailIfStdError)
    .ExecuteAsync();
````

## Cancellation

````c#
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var process = ProcessWrapper.Create("long-running-process")
    .ExecuteAsync(cts.Token);

await process; // throws OperationCanceledException if cancelled
````

## Killing a process

````c#
var process = ProcessWrapper.Create("long-running-process")
    .WithValidation(ProcessValidationMode.None)
    .ExecuteAsync();

process.Kill(); // or process.Kill(entireProcessTree: false)
await process;
````

## Reusable configuration

The builder is immutable, so you can create a base configuration and reuse it:

````c#
var baseCommand = ProcessWrapper.Create("dotnet")
    .WithWorkingDirectory("/path/to/repo")
    .AddErrorStream(line => Console.Error.WriteLine(line));

var build = baseCommand.WithArguments("build").ExecuteAsync();
await build;

var test = baseCommand.WithArguments("test").ExecuteAsync();
await test;
````
