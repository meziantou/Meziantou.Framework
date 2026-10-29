---
title: Exit codes and errors
description: Check the exit code of a process, throw on errors or on output to standard error, and control what error messages show.
---

## Validation

By default, `ExecuteAsync` and `ExecuteBufferedAsync` throw a `ProcessExecutionException` when the process exits with
a non-zero exit code. `WithValidation` changes the rules:

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

| `ProcessValidationMode` | Throws when |
| --- | --- |
| `FailIfNonZeroExitCode` (default) | The exit code is not 0 |
| `FailIfStdError` | The process wrote anything to standard error |
| `None` | Never: check the exit code yourself |

The values are flags: combine them with `|`. The exception is thrown when you await the process, after it exited and
all its output was read.

## The exception

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_ExecutionException;format=md-fenced-code-block;dedent=true -->
```csharp
var command = ProcessWrapper.Create("my-command")
    .WithProcessFactory(new FakeProcessFactory(FakeProcess.Create(exitCode: 1)));

var exception = await Assert.ThrowsAsync<ProcessExecutionException>(async () => await command.ExecuteAsync());
Assert.Equal(1, exception.ExitCode.Value);
Assert.StartsWith("Process exited with code 1.", exception.Message);
```
<!-- endref -->

- For an exit code failure, `ExitCode` holds the exit code, and the message starts with `Process exited with code`.
- For output on standard error, the message starts with `Process wrote to standard error.`
- The message ends with the command, as allowed by the [log verbosity](#what-messages-show).

## Read the exit code

With `ProcessValidationMode.None`, read the exit code from the result:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_ExitCodes;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-command")
    .WithProcessFactory(new FakeProcessFactory(FakeProcess.Create(exitCode: 3)))
    .WithValidation(ProcessValidationMode.None)
    .ExecuteAsync();

Assert.False(result.ExitCode.IsSuccess);
Assert.True(result.ExitCode.IsAnyOf(2, 3));

// ProcessExitCode converts implicitly to int
int exitCode = result.ExitCode;
Assert.Equal(3, exitCode);
```
<!-- endref -->

`ProcessExitCode` wraps the value: `IsSuccess` is `true` for 0, `IsAnyOf` checks a list of expected values, `Value` and
the implicit conversion to `int` return the number.

## What messages show

Exception messages and `ProcessResult.ToString` include the command. Arguments can contain secrets, such as tokens or
passwords, so by default only the path of the executable is shown. `ProcessLogVerbosity` chooses what is shown:

| `ProcessLogVerbosity` | Shows |
| --- | --- |
| `None` | Nothing |
| `IncludeProcessPath` (default) | The resolved path of the executable |
| `IncludeArguments` | The arguments |

Combine `IncludeProcessPath | IncludeArguments` to show the whole command line. Set it per command with
`WithLogVerbosity`, or for all commands with `ProcessWrapper.DefaultLogVerbosity`. The default applies to the
`ProcessWrapper` instances created after you set it.

`ProcessWrapper.ToString` is not affected: it always returns the whole command line. See
[Configuring the process](03-ConfiguringTheProcess.md#arguments).
