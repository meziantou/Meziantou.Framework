---
title: Introduction
description: A fluent API to configure and run processes, read their output, validate their exit code and fake them in tests.
---

`Meziantou.Framework.ProcessWrapper` runs processes. You describe the command with a fluent API, start it, and await
the result. The package takes care of the parts that are easy to get wrong with `System.Diagnostics.Process`: reading
standard output and standard error at the same time without deadlocking, decoding them line by line, passing arguments
without quoting them, killing the process when the operation is canceled, and checking the exit code.

## What you can do with it

- **Run a command and wait for it**, with its arguments, working directory and environment variables. See
  [Getting started](02-GettingStarted.md) and [Configuring the process](03-ConfiguringTheProcess.md).
- **Read the output** line by line, into a collection, a `StringBuilder`, a `TextWriter` or a stream, or capture all of
  it. See [Reading the output](04-ReadingTheOutput.md).
- **Write to the standard input**, and connect processes with a pipe or the `|` operator. See
  [Input and pipes](05-InputAndPipes.md).
- **Validate the result**: throw on a non-zero exit code or on output on standard error. See
  [Exit codes and errors](06-ExitCodesAndErrors.md).
- **Cancel or kill** a running process. See [Cancellation and killing](07-CancellationAndKilling.md).
- **Limit CPU, memory and the number of processes** with Windows Job Objects or Linux control groups. See
  [Resource limits](08-ResourceLimits.md).
- **Replace processes in tests** with fake processes, and change commands globally with interceptors. See
  [Testing](09-Testing.md).
- **Trace executions** with `System.Diagnostics` activities. See [Telemetry](10-Telemetry.md).

## Namespace

The types are in the [Meziantou.Framework](cref:Meziantou.Framework.ProcessWrapper!N:Meziantou.Framework) namespace. The main ones:

| Type | Contains |
| --- | --- |
| [ProcessWrapper](cref:Meziantou.Framework.ProcessWrapper) | The command to run, its configuration, and the methods that start it |
| [ProcessInstance](cref:Meziantou.Framework.ProcessInstance), [BufferedProcessInstance](cref:Meziantou.Framework.BufferedProcessInstance) | A running process, to await or kill |
| [ProcessResult](cref:Meziantou.Framework.ProcessResult), [BufferedProcessResult](cref:Meziantou.Framework.BufferedProcessResult) | The exit code and dates of a process that exited, and its output when buffered |
| [ProcessExitCode](cref:Meziantou.Framework.ProcessExitCode) | An exit code |
| [OutputTarget](cref:Meziantou.Framework.OutputTarget), [InputSource](cref:Meziantou.Framework.InputSource) | Where the output goes, and where the input comes from |
| [ProcessOutputCollection](cref:Meziantou.Framework.ProcessOutputCollection), [ProcessOutput](cref:Meziantou.Framework.ProcessOutput) | Captured output lines |
| [ProcessPipe](cref:Meziantou.Framework.ProcessPipe), [ProcessPipeline](cref:Meziantou.Framework.ProcessPipeline) | Connections between processes |
| [ProcessLimits](cref:Meziantou.Framework.ProcessLimits) | The CPU, memory and process count limits |
| [FakeProcess](cref:Meziantou.Framework.FakeProcess), [FakeProcessFactory](cref:Meziantou.Framework.FakeProcessFactory) | In-memory processes for tests |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and run your first command.
