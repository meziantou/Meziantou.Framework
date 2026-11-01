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
| `OpenReadAsync(path)` | Reads a file of the container, as a stream to dispose once read. |

Files are streamed, never loaded in memory, so copying a database dump or a large artifact does not cost its size in
memory:

- With the Docker Engine API, the stream [OpenReadAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.OpenReadAsync) returns reads the file from the daemon as you read it. It cannot
  seek, but its `Length` is known: copy it to a `MemoryStream` when you need to seek. The CLI runtimes copy the file to a
  temporary file first, and return a seekable stream that deletes it when disposed.
- [WriteFileAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.WriteFileAsync) reads the content from its current position. With the Docker Engine API, a seekable stream is
  sent as it is read; a stream whose length is unknown (a network stream, a pipe) is copied to a temporary file first,
  because the archive sent to the daemon declares the length of the file before its content.
- The build context of a [Dockerfile image](04-Configuration/02-Images.md) is sent to the daemon as it is archived.

The temporary files are only readable by the current user, since a file can hold secrets.

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

To read only part of the logs, pass a [ContainerLogsOptions](cref:Meziantou.Framework.TemporaryContainers.ContainerLogsOptions):

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_LogsOptions;format=md-fenced-code-block;dedent=true -->
```csharp
// The last 50 lines of the standard error written in the last 5 minutes, without following the new ones
var options = new ContainerLogsOptions
{
    Since = DateTimeOffset.UtcNow.AddMinutes(-5),
    Tail = 50,
    Follow = false,
    IncludeStandardOutput = false,
};

await foreach (var entry in container.GetLogsAsync(options))
{
    Console.WriteLine($"{entry.Timestamp:O} {entry.Message}");
}
```
<!-- endref -->

| Option | Effect |
| --- | --- |
| [Since](cref:Meziantou.Framework.TemporaryContainers.ContainerLogsOptions.Since), [Until](cref:Meziantou.Framework.TemporaryContainers.ContainerLogsOptions.Until) | Return the entries written in this time range, bounds included. |
| [Tail](cref:Meziantou.Framework.TemporaryContainers.ContainerLogsOptions.Tail) | Return the last entries only. The runtime takes them first, then applies the time range and the stream selection. |
| [Follow](cref:Meziantou.Framework.TemporaryContainers.ContainerLogsOptions.Follow) | Keep returning the new entries (the default), or end with the entries written so far. |
| [IncludeStandardOutput](cref:Meziantou.Framework.TemporaryContainers.ContainerLogsOptions.IncludeStandardOutput), [IncludeStandardError](cref:Meziantou.Framework.TemporaryContainers.ContainerLogsOptions.IncludeStandardError) | Select the streams. At least one must be included. |

The times are compared with the timestamps the runtime records, which come from the clock of the runtime: `wslc`
reports the time of its virtual machine, which can differ from the time of the host. Apple's `container` records no
timestamps, so it does not support `Since` and `Until` and throws a `NotSupportedException`.

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
fails the container. A container started again, or restarted, keeps the logs of its earlier runs: only the entries of
the current run are forwarded, and the [log wait strategy](04-Configuration/06-WaitStrategies.md) only looks for its
message there. When the runtime ends the log stream while the container runs, the forwarding attaches again from the
last entry it forwarded. To write them to the output of an xUnit test, see
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
| [InspectAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.InspectAsync) | Returns a [ContainerInfo](cref:Meziantou.Framework.TemporaryContainers.ContainerInfo): id, name, image, [State](cref:Meziantou.Framework.TemporaryContainers.ContainerInfo.State), [Health](cref:Meziantou.Framework.TemporaryContainers.ContainerInfo.Health), exit code, start and finish times, IP address, ports and labels. |
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
