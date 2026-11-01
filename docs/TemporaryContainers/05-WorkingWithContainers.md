---
title: Working with a container
description: Run commands in a container, copy files in and out, read and forward its logs, and stop, restart or inspect it.
---

A started [TemporaryContainer](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer) lets a test act on the container: prepare data, check what the service wrote, or
simulate an outage.

## Run a command

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Exec;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await container.ExecAsync(options =>
{
    options.Command = ["psql", "--username", "postgres", "--tuples-only", "--command", "SELECT version()"];
    options.Environment.Add("PGCONNECT_TIMEOUT", "5");
});

// A command that fails does not throw: check its exit code
if (result.ExitCode != 0)
    throw new InvalidOperationException("psql failed: " + result.StandardError);

var version = result.StandardOutput.Trim();
```
<!-- endref -->

- [ExecOptions.Command](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.Command) is the executable and its arguments, passed as is, without a shell.
- [ExecOptions](cref:Meziantou.Framework.TemporaryContainers.ExecOptions) also sets the [Environment](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.Environment), the [User](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.User) and the [WorkingDirectory](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.WorkingDirectory) of the command.
- [ExecAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.ExecAsync) returns once the command exits, with its [ExitCode](cref:Meziantou.Framework.TemporaryContainers.ExecResult.ExitCode), [StandardOutput](cref:Meziantou.Framework.TemporaryContainers.ExecResult.StandardOutput) and [StandardError](cref:Meziantou.Framework.TemporaryContainers.ExecResult.StandardError). A command that
  fails does not throw; a runtime that cannot run it throws a [ContainerRuntimeException](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException).

To send data to the standard input of the command, set [StandardInput](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.StandardInput):

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ExecStandardInput;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await container.ExecAsync(options =>
{
    options.Command = ["psql", "--username", "postgres"];
    options.StandardInput = InputSource.FromText("CREATE TABLE orders (id int PRIMARY KEY);");
});
```
<!-- endref -->

[InputSource](cref:Meziantou.Framework.InputSource) also reads from a stream, bytes or a file. The Docker Engine API runtime cannot send the standard input to
a daemon reached over a named pipe, the default on Windows: use the `docker` runtime there.

### Fail when the command fails

[EnsureSuccess](cref:Meziantou.Framework.TemporaryContainers.ExecResult.EnsureSuccess) throws a [ContainerExecException](cref:Meziantou.Framework.TemporaryContainers.ContainerExecException) when the exit code is not 0. Its message has the command
and the standard error, so a failed setup step explains itself in the test output. [Timeout](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.Timeout) bounds the time
[ExecAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.ExecAsync) waits:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ExecEnsureSuccess;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await container.ExecAsync(options =>
{
    options.Command = ["psql", "--username", "postgres", "--command", "CREATE DATABASE app"];
    options.Timeout = TimeSpan.FromSeconds(30); // throws a TimeoutException after 30 seconds
});

result.EnsureSuccess(); // throws a ContainerExecException, with the standard error, when the exit code is not 0
```
<!-- endref -->

The runtimes cannot stop a command they started without a terminal: after the timeout, the command may keep running in
the container.

### Binary or large output

[StandardOutput](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.StandardOutput) and [StandardError](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.StandardError) copy the outputs to a stream, byte for byte, instead of decoding them as text.
Use them for a database dump or an archive, which decoding would corrupt, or for an output too large to keep in memory:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ExecBinaryOutput;format=md-fenced-code-block;dedent=true -->
```csharp
await using var dump = File.Create("database.dump");
var result = await container.ExecAsync(options =>
{
    options.Command = ["pg_dump", "--username", "postgres", "--format", "custom", "postgres"];
    options.StandardOutput = dump; // copied byte for byte, never decoded as text
});

result.EnsureSuccess();
```
<!-- endref -->

The matching property of the [ExecResult](cref:Meziantou.Framework.TemporaryContainers.ExecResult) is then empty. The streams are not disposed.

### Background commands, terminals and privileges

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ExecDetach;format=md-fenced-code-block;dedent=true -->
```csharp
await container.ExecAsync(options =>
{
    options.Command = ["sh", "-c", "seed-data.sh > /tmp/seed.log 2>&1"];
    options.Detach = true; // returns as soon as the command is started
});
```
<!-- endref -->

- [Detach](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.Detach) starts the command and returns at once, with an exit code of 0 and no output. A detached command cannot have a
  standard input, an output stream or a timeout.
- [Tty](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.Tty) runs the command in a pseudo-terminal, for the commands that only print colors or progress, or only behave
  interactively, in one. A terminal merges the standard error into the standard output and ends lines with `\r\n`.
- [Privileged](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.Privileged) runs the command with every capability. Apple's `container` does not support it.

## Copy files

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Files;format=md-fenced-code-block;dedent=true -->
```csharp
using var settings = new MemoryStream("""{ "mode": "test" }"""u8.ToArray());
await container.WriteFileAsync("/tmp/settings.json", settings); // creates or overwrites the file

await container.CopyToContainerAsync("testdata", "/tmp/testdata"); // a file or a directory
await container.CopyFromContainerAsync("/var/log", "artifacts/container-logs");

await using var stream = await container.OpenReadAsync("/etc/os-release");
using var reader = new StreamReader(stream);
var osRelease = await reader.ReadToEndAsync();
```
<!-- endref -->

| Method | Effect |
| --- | --- |
| `WriteFileAsync(path, content)` | Creates or overwrites a file of the container with the content of a stream. |
| `CopyToContainerAsync(source, destination)` | Copies a file or a directory of the host into the container. |
| `CopyFromContainerAsync(source, destination)` | Copies a file or a directory of the container to the host. |
| `OpenReadAsync(path)` | Reads a file of the container, as a seekable stream. |

To give files to the container before it starts, a [bind mount](04-Configuration/05-VolumesAndMounts.md) is often
simpler.

## Read the logs

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Logs;format=md-fenced-code-block;dedent=true -->
```csharp
// Follows the logs from the creation of the container until it stops: break or cancel to stop earlier
await foreach (var entry in container.GetLogsAsync())
{
    Console.WriteLine($"[{entry.Stream}] {entry.Message}");
    if (entry.Message.Contains("checkpoint complete", StringComparison.Ordinal))
        break;
}
```
<!-- endref -->

[GetLogsAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetLogsAsync) returns the logs from the creation of the container, then follows the new lines until the container
stops. On a running container, the loop does not end on its own: break out of it, or pass a `CancellationToken`. Each
[LogEntry](cref:Meziantou.Framework.TemporaryContainers.LogEntry) has the [Message](cref:Meziantou.Framework.TemporaryContainers.LogEntry.Message), the [Stream](cref:Meziantou.Framework.TemporaryContainers.LogEntry.Stream) it was written to ([LogStream.Stdout](cref:Meziantou.Framework.TemporaryContainers.LogStream.Stdout) or [LogStream.Stderr](cref:Meziantou.Framework.TemporaryContainers.LogStream.Stderr)) and, when the
runtime reports it, a [Timestamp](cref:Meziantou.Framework.TemporaryContainers.LogEntry.Timestamp).

## Forward the logs to a logger

To see what the container prints, for instance in the output of a failed test, give the definition an `ILogger`:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ForwardLogs;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = ContainerDefinition.CreateRedis();
definition.Logging.Logger = logger; // standard output is logged as Information, standard error as Error
definition.Logging.CaptureStandardOutput = false; // forward standard error only
```
<!-- endref -->

The lines are forwarded while the container runs, from [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) until it stops or is disposed: standard output at
the `Information` level, standard error at the `Error` level. A logger that throws stops the forwarding, but never
fails the container. To write them to the output of an xUnit test, see
[Using containers in tests and CI](09-TestFrameworksAndCI.md#see-the-container-logs-in-the-test-output).

## Stop, restart and inspect

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Lifecycle;format=md-fenced-code-block;dedent=true -->
```csharp
await container.StopAsync();
await container.StartAsync(); // starts it again and runs the wait strategies

await container.RestartAsync();
await container.WaitUntilReadyAsync(); // a restart does not run the wait strategies

await container.PauseAsync();
await container.UnpauseAsync();

var info = await container.InspectAsync();
Console.WriteLine($"{info.Name}: {info.State}");

await container.KillAsync();
await container.DeleteAsync(); // removes it now; a later StartAsync creates a new container
```
<!-- endref -->

| Method | Effect |
| --- | --- |
| [StopAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StopAsync) | Stops the container. It keeps its file system; [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) starts it again. |
| [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) | Creates the container if needed, starts it, and runs the wait strategies. |
| [RestartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.RestartAsync) | Restarts the container. It does not run the wait strategies: call [WaitUntilReadyAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.WaitUntilReadyAsync) after it. |
| [PauseAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.PauseAsync), [UnpauseAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.UnpauseAsync) | Freezes and resumes the processes of the container, to simulate a service that hangs. |
| [KillAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.KillAsync) | Sends a kill signal to the container, to simulate a crash. |
| [InspectAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.InspectAsync) | Returns a [ContainerInfo](cref:Meziantou.Framework.TemporaryContainers.ContainerInfo): id, name, image, [State](cref:Meziantou.Framework.TemporaryContainers.ContainerInfo.State), exit code, start and finish times, IP address, ports and labels. |
| [ExistsAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.ExistsAsync) | Tells whether the container still exists. |
| [DeleteAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.DeleteAsync) | Removes the container, and the image built for it, right away. A later [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) creates a new container. |

Apple's `container` has no pause: [PauseAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.PauseAsync) and [UnpauseAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.UnpauseAsync) throw a `NotSupportedException` there. It has no
restart either, nor has `wslc`: [RestartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.RestartAsync) stops then starts the container.

## Create a container without starting it

[EnsureCreatedAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.EnsureCreatedAsync) creates the container, or adopts the one a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) designates, without starting it. [Id](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.Id) and
[Name](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.Name) are available from then on:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_EnsureCreated;format=md-fenced-code-block;dedent=true -->
```csharp
await using var container = ContainerDefinition.CreateRedis().CreateContainer();
await container.EnsureCreatedAsync(); // creates the container without starting it
Console.WriteLine(container.Name);
```
<!-- endref -->
