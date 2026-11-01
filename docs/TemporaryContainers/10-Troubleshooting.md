---
title: Troubleshooting
description: The exceptions the library throws, what they tell you, and how to fix the common failures.
---

## The exceptions

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ErrorHandling;format=md-fenced-code-block;dedent=true -->
```csharp
try
{
    await container.StartAsync();
}
catch (ContainerRuntimeException ex)
{
    // The runtime refused an operation: a CLI command (Command, ExitCode, StandardError) or a Docker Engine API request (StatusCode)
    Console.WriteLine($"{ex.Runtime} failed: {ex.Command} {ex.StatusCode} {ex.StandardError}");
    throw;
}
catch (TimeoutException ex)
{
    // Not ready within StartupTimeout: the message names the wait strategy, the state of the container and its last log lines
    Console.WriteLine(ex.Message);
    throw;
}
```
<!-- endref -->

| Exception | Thrown when |
| --- | --- |
| [ContainerRuntimeException](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException) | The runtime refused an operation. [Runtime](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException.Runtime) is the runtime; [Command](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException.Command), [ExitCode](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException.ExitCode), [StandardOutput](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException.StandardOutput) and [StandardError](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException.StandardError) describe the command line that failed; [StatusCode](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException.StatusCode) the request the Docker Engine API refused. |
| `TimeoutException` | The wait strategies did not complete within [StartupTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.StartupTimeout). The message names the strategy that did not complete, the state of the container and its last log lines. |
| `InvalidOperationException` | No runtime is available; the container exited while a log message strategy waited (the message gives its exit code and last log lines); a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) designates a container created from another definition; or the container was used before [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) ([Id](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.Id), [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort), `GetConnectionString`). |
| `NotSupportedException` | The runtime cannot do the operation: pausing with Apple's `container`, volumes with `wslc`, a reaper with either. |

## No container runtime is available

[StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) throws "No supported container runtime ... is available":

- Check that the runtime is installed and that its command line is on the `PATH` of the test process. A test runner
  started by an IDE does not always have the `PATH` of your terminal.
- Check that its daemon runs: start Docker Desktop or the Podman machine, or run `container system start` for Apple's
  `container`.
- With the Docker Engine API runtime, read the end of the message: it lists the endpoints the runtime tried and why each
  one failed. Check `DOCKER_HOST`, `DOCKER_CONTEXT` and the current docker context (`docker context ls`). See
  [Connect to a Docker daemon](06-Runtimes.md#connect-to-a-docker-daemon).

## The container is never ready

The `TimeoutException` says which strategy did not complete and shows the last log lines:

- **The log message changed.** A new version of the image may print another message: pin the image tag, and update the
  strategy along with it.
- **The message is printed twice.** Some images start the service twice during their first start; count the
  occurrences or add [Wait.ForPort](cref:Meziantou.Framework.TemporaryContainers.Wait.ForPort). See [Wait strategies](04-Configuration/06-WaitStrategies.md).
- **The container exited.** Look at the last log lines: a missing environment variable or a wrong command line usually
  shows there.
- **The machine is slow.** Pulling is not part of the timeout, but the start of a large service on a loaded build agent
  can be: raise [StartupTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.StartupTimeout).
- **The image does not run on the machine.** An image published for x64 only, such as SQL Server, does not run on an
  arm64 machine unless the runtime emulates x64.

## A port cannot be reached

- Use the host port: `GetMappedPort(containerPort)` returns it. The container port is only reachable from the network
  of the container.
- Connect to `127.0.0.1`: ports are published on the loopback address by default. See [Ports](04-Configuration/04-Ports.md).
- Read the port again after [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) or [RestartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.RestartAsync).

## Containers are left behind

- A test run that is killed does not dispose its containers: call [CleanupAsync](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.CleanupAsync) when the tests start, and start a
  reaper. See [Cleaning up](07-Cleanup.md).
- A container that has a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) is kept on purpose. See [Reusing a container](08-ReusingContainers.md).
- With `wslc`, the images built from a Dockerfile and the anonymous volumes are not removed.

## See what the container does

- Forward its logs to a logger or to the test output. See
  [Forward the logs to a logger](05-WorkingWithContainers.md#forward-the-logs-to-a-logger).
- Inspect it with [InspectAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.InspectAsync), or run a command in it with [ExecAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.ExecAsync). See
  [Working with a container](05-WorkingWithContainers.md).
- To look at it with the tools of the runtime, `container.Name` and `container.Id` identify it.
