---
title: Advanced options
description: Capabilities, extra hosts, shared memory, ulimits, healthchecks, stop signal, and options the definition does not have.
---

These options are less common than the ones of [Command, environment and resources](03-CommandAndEnvironment.md). They are
all set on the [ContainerDefinition](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition), and are part of what a [reused container](../08-ReusingContainers.md) is matched against.

## Capabilities and privileged mode

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_SecurityOptions;format=md-fenced-code-block;dedent=true -->
```csharp
definition.CapAdd.Add("NET_ADMIN");
definition.CapDrop.Add("MKNOD");
definition.Privileged = true; // every capability, and the devices of the host
```
<!-- endref -->

[CapAdd](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.CapAdd) and [CapDrop](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.CapDrop) take Linux capability names, such as `NET_ADMIN` or `ALL`.
[Privileged](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Privileged) gives the container every capability and the devices of the host: use it only for images that
need it, such as Docker-in-Docker.

## Reach the host from the container

A container calls back to the test process through the host. Docker Desktop resolves `host.docker.internal` on its
own; on Linux, add it with [ContainerExtraHost.ForHostGateway](cref:Meziantou.Framework.TemporaryContainers.ContainerExtraHost.ForHostGateway). [ExtraHosts](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ExtraHosts) adds any other entry to
`/etc/hosts`:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ExtraHosts;format=md-fenced-code-block;dedent=true -->
```csharp
// host.docker.internal resolves to the machine that runs the tests, on Linux too
definition.ExtraHosts.Add(ContainerExtraHost.ForHostGateway());
definition.ExtraHosts.Add(new ContainerExtraHost("api.test", "10.0.0.5"));
```
<!-- endref -->

The test process must listen on an address the container can reach: a server bound to `127.0.0.1` only is not
reachable from a container.

## Shared memory and ulimits

Browsers and some databases need more shared memory than the 64 MB docker gives by default, or higher limits on open
files:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_SharedMemoryAndUlimits;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Resources.SharedMemorySize = 1024L * 1024 * 1024; // /dev/shm, in bytes
definition.Resources.Ulimits.Add(new ContainerUlimit("nofile", 65536)); // soft and hard limit
definition.Resources.Ulimits.Add(new ContainerUlimit("nproc", 1024, 2048)); // soft limit, hard limit
```
<!-- endref -->

## Healthcheck

[Healthcheck](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Healthcheck) replaces the `HEALTHCHECK` the image declares, or adds one to an image that has none:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Healthcheck;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Healthcheck = new ContainerHealthcheck("pg_isready -U postgres")
{
    Interval = TimeSpan.FromSeconds(1),
    Timeout = TimeSpan.FromSeconds(5),
    StartPeriod = TimeSpan.FromSeconds(10),
    Retries = 30,
};
```
<!-- endref -->

- `new ContainerHealthcheck("command")` runs the command through the shell of the container (`CMD-SHELL`).
  `new ContainerHealthcheck(["executable", "argument"])` runs an executable without a shell (`CMD`) with the Docker
  Engine API; the CLI runtimes only take a shell command, so they run the arguments, quoted, through the shell.
- The durations and the number of retries keep the defaults of the runtime when they are not set.
- The healthcheck only tells the runtime whether the container is healthy. Use a [wait strategy](06-WaitStrategies.md) to make
  [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) wait for the service.

To ignore the healthcheck of the image:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_DisableHealthcheck;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Healthcheck = ContainerHealthcheck.Disabled; // ignore the HEALTHCHECK of the image
```
<!-- endref -->

## Stop signal and timeout

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_StopOptions;format=md-fenced-code-block;dedent=true -->
```csharp
definition.StopSignal = "SIGINT"; // the signal StopAsync sends
definition.StopTimeout = TimeSpan.FromSeconds(30); // how long the runtime waits before it kills the container
```
<!-- endref -->

[StopTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.StopTimeout) is rounded down to whole seconds. Docker kills a container that is still running 10 seconds after the
signal when it is not set.

## Options the definition does not have

When an option of the runtime has no property, pass it yourself. The CLI runtimes add
[AdditionalCreateArguments](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.AdditionalCreateArguments) to their `create` command, right before the image; the Docker Engine API calls
[DockerApiCreateRequestCustomizer](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.DockerApiCreateRequestCustomizer) with the JSON body of `POST /containers/create` before it sends it:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_EscapeHatches;format=md-fenced-code-block;dedent=true -->
```csharp
// The CLI runtimes (docker, podman, container, wslc): arguments added before the image
definition.AdditionalCreateArguments.Add("--init");

// The Docker Engine API: the JSON body of POST /containers/create
definition.DockerApiCreateRequestCustomizer = request => request["HostConfig"]!["Init"] = true;
```
<!-- endref -->

- Each runtime ignores the setting of the other kind, so set both when the runtime is detected automatically.
- The library does not check what you pass: an argument the runtime does not know makes the creation fail.
- The customizer is not part of what a [reused container](../08-ReusingContainers.md) is matched against, since the library
  cannot compare two callbacks. Change the [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) when you change what it does.

## Runtime differences

| Option | Docker Engine API, Docker, Podman | Apple container |
| --- | --- | --- |
| Capabilities, shared memory, ulimits | Yes | Yes |
| Privileged mode, extra hosts, healthcheck, stop signal and timeout | Yes | No |

`wslc` gets the same arguments as docker, but is not known to support all of them. A runtime that cannot apply an option throws a `NotSupportedException` when the container is created, rather than
creating a container that does not match its definition.
