---
title: Getting started
description: Install the package, run a command, wait for it, and capture its output.
---

## Install the package

Add the `Meziantou.Framework.ProcessWrapper` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.ProcessWrapper" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Run a command

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_BasicUsage;format=md-fenced-code-block;dedent=true -->
```csharp
// Execute and wait for exit (throws if exit code is non-zero by default)
var result = await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .ExecuteAsync();

Assert.True(result.ExitCode.IsSuccess);
```
<!-- endref -->

- [ProcessWrapper.Create](cref:Meziantou.Framework.ProcessWrapper.Create) takes the executable: a name that is looked up in `PATH`, or a path.
- [WithArguments](cref:Meziantou.Framework.ProcessWrapper.WithArguments) sets the arguments. Each argument is passed to the process as is: there is nothing to quote or escape.
- [ExecuteAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteAsync) starts the process and returns a [ProcessInstance](cref:Meziantou.Framework.ProcessInstance) at once. Awaiting it waits for the process to exit
  and returns a [ProcessResult](cref:Meziantou.Framework.ProcessResult) with the exit code, the process id, and the start and exit dates.
- By default, a non-zero exit code throws a [ProcessExecutionException](cref:Meziantou.Framework.ProcessExecutionException). See
  [Exit codes and errors](06-ExitCodesAndErrors.md).

## Capture the output

[ExecuteBufferedAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteBufferedAsync) also collects the lines the process writes to standard output and standard error:

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

`result.Output` is a [ProcessOutputCollection](cref:Meziantou.Framework.ProcessOutputCollection): every line, in the order it was received, with the stream it comes from
and the time it was received. [StandardOutput](cref:Meziantou.Framework.ProcessOutputCollection.StandardOutput) and [StandardError](cref:Meziantou.Framework.ProcessOutputCollection.StandardError) filter it by stream. To process the lines while the
process runs instead, see [Reading the output](04-ReadingTheOutput.md).

## The builder changes in place

The `With*` and `Add*` methods change the [ProcessWrapper](cref:Meziantou.Framework.ProcessWrapper) they are called on, and return it so you can chain the
calls. They do not create a copy: two variables that reference the same [ProcessWrapper](cref:Meziantou.Framework.ProcessWrapper) see the same configuration.
Create one [ProcessWrapper](cref:Meziantou.Framework.ProcessWrapper) per command, or configure it completely before sharing it.

You can execute the same [ProcessWrapper](cref:Meziantou.Framework.ProcessWrapper) several times: each [ExecuteAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteAsync) call starts a new process.

## Next steps

- [Configuring the process](03-ConfiguringTheProcess.md): the working directory, the environment variables and the
  other options.
- [Reading the output](04-ReadingTheOutput.md): stream the output while the process runs.
- [Testing](09-Testing.md): run your code without starting real processes.
