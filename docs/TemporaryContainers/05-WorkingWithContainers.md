---
title: Working with a container
description: Run commands in a container, copy files in and out, read and forward its logs, and stop, restart or inspect it.
---

A started `TemporaryContainer` lets a test act on the container: prepare data, check what the service wrote, or
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

- `ExecOptions.Command` is the executable and its arguments, passed as is, without a shell.
- `ExecOptions` also sets the `Environment`, the `User` and the `WorkingDirectory` of the command.
- `ExecAsync` returns once the command exits, with its `ExitCode`, `StandardOutput` and `StandardError`. A command that
  fails does not throw; a runtime that cannot run it throws a `ContainerRuntimeException`.

To send data to the standard input of the command, set `StandardInput`:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ExecStandardInput;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await container.ExecAsync(options =>
{
    options.Command = ["psql", "--username", "postgres"];
    options.StandardInput = InputSource.FromText("CREATE TABLE orders (id int PRIMARY KEY);");
});
```
<!-- endref -->

`InputSource` also reads from a stream, bytes or a file. The Docker Engine API runtime cannot send the standard input to
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

`GetLogsAsync` returns the logs from the creation of the container, then follows the new lines until the container
stops. On a running container, the loop does not end on its own: break out of it, or pass a `CancellationToken`. Each
`LogEntry` has the `Message`, the `Stream` it was written to (`LogStream.Stdout` or `LogStream.Stderr`) and, when the
runtime reports it, a `Timestamp`.

## Forward the logs to a logger

To see what the container prints, for instance in the output of a failed test, give the definition an `ILogger`:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ForwardLogs;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = ContainerDefinition.CreateRedis();
definition.Logging.Logger = logger; // standard output is logged as Information, standard error as Error
definition.Logging.CaptureStandardOutput = false; // forward standard error only
```
<!-- endref -->

The lines are forwarded while the container runs, from `StartAsync` until it stops or is disposed: standard output at
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
| `StopAsync` | Stops the container. It keeps its file system; `StartAsync` starts it again. |
| `StartAsync` | Creates the container if needed, starts it, and runs the wait strategies. |
| `RestartAsync` | Restarts the container. It does not run the wait strategies: call `WaitUntilReadyAsync` after it. |
| `PauseAsync`, `UnpauseAsync` | Freezes and resumes the processes of the container, to simulate a service that hangs. |
| `KillAsync` | Sends a kill signal to the container, to simulate a crash. |
| `InspectAsync` | Returns a `ContainerInfo`: id, name, image, `State`, exit code, start and finish times, IP address, ports and labels. |
| `ExistsAsync` | Tells whether the container still exists. |
| `DeleteAsync` | Removes the container, and the image built for it, right away. A later `StartAsync` creates a new container. |

Apple's `container` has no pause: `PauseAsync` and `UnpauseAsync` throw a `NotSupportedException` there. It has no
restart either, nor has `wslc`: `RestartAsync` stops then starts the container.

## Create a container without starting it

`EnsureCreatedAsync` creates the container, or adopts the one a `ReuseId` designates, without starting it. `Id` and
`Name` are available from then on:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_EnsureCreated;format=md-fenced-code-block;dedent=true -->
```csharp
await using var container = ContainerDefinition.CreateRedis().CreateContainer();
await container.EnsureCreatedAsync(); // creates the container without starting it
Console.WriteLine(container.Name);
```
<!-- endref -->
