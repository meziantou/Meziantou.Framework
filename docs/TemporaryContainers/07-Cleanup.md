---
title: Cleaning up
description: How containers, volumes and built images are removed, and how to remove what an interrupted test run left behind.
---

## Dispose the container

Disposing a [TemporaryContainer](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer) removes the container and the image built for it from a Dockerfile. Use `await using`,
or dispose it in the teardown of your test fixture:

- Disposing never throws and is bounded in time: a runtime that does not answer cannot block the end of a test run.
- A container that has a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) is kept. See [Reusing a container](08-ReusingContainers.md).
- Disposing a [TemporaryVolume](cref:Meziantou.Framework.TemporaryContainers.TemporaryVolume) removes the volume, when the library created it.

## Remove the leftovers of interrupted runs

A test run that is stopped in the debugger or killed never disposes its containers. Every container, volume and built
image the library creates is labeled with the process that created it, so a later run can find these leftovers and
remove them, and only them:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_Cleanup;format=md-fenced-code-block;dedent=true -->
```csharp
// Removes the containers, images and volumes whose creating process is gone. Nothing else is touched:
// not the resources of a live process, not those created on another machine, not the reused ones.
var result = await ContainerRuntime.Auto.CleanupAsync();
Console.WriteLine($"{result.RemovedCount} leftovers removed");
```
<!-- endref -->

Call it once when a test run starts: an xUnit assembly fixture (see
[Using containers in tests and CI](09-TestFrameworksAndCI.md#remove-the-leftovers-before-the-tests)), a
`[ModuleInitializer]`, or a script.

- By default, it removes the resources whose creating process is gone. A process is identified by its machine, its id
  and its start time, so a process whose id was reused is told apart.
- The resources of a process that is still running, those created on another machine, and those that have a
  [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) are left alone.
- The resources of the current process are never removed, whatever the options: a cleanup at the start of a run cannot
  remove the containers that run is about to use.
- The result lists what was removed ([RemovedContainers](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupResult.RemovedContainers), [RemovedImages](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupResult.RemovedImages), [RemovedVolumes](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupResult.RemovedVolumes), [RemovedCount](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupResult.RemovedCount)) and the
  failures in [Errors](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupResult.Errors): a resource that cannot be removed does not stop the cleanup.

## Choose what to remove

[ContainerCleanupOptions](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupOptions) widens or narrows the cleanup:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_CleanupOptions;format=md-fenced-code-block;dedent=true -->
```csharp
await ContainerRuntime.Docker.CleanupAsync(new ContainerCleanupOptions
{
    Scope = ContainerCleanupScope.All,          // every resource of this library, not only the orphaned ones
    MinimumAge = TimeSpan.FromHours(1),         // ... that is at least one hour old
    IncludeVolumes = false,                     // ... not the volumes
    IncludeImages = false,                      // ... nor the images built from a Dockerfile
    IncludeReusedResources = true,              // ... including the containers kept alive by a ReuseId
});
```
<!-- endref -->

| Option | Default | Effect |
| --- | --- | --- |
| [Scope](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupOptions.Scope) | [Orphaned](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupScope.Orphaned) | [Orphaned](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupScope.Orphaned) removes the resources whose creating process is gone; [All](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupScope.All) removes every resource of the library, except those of the current process. |
| [MinimumAge](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupOptions.MinimumAge) | `TimeSpan.Zero` | Only removes the resources at least this old. |
| [IncludeContainers](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupOptions.IncludeContainers) | `true` | Removes containers. |
| [IncludeVolumes](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupOptions.IncludeVolumes) | `true` | Removes volumes, after the containers. |
| [IncludeImages](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupOptions.IncludeImages) | `true` | Removes the images built from a Dockerfile, after the containers. `wslc` does not label them, so they are not found. |
| [IncludeReusedResources](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupOptions.IncludeReusedResources) | `false` | Also removes the resources that have a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId). |

[ContainerCleanupScope.All](cref:Meziantou.Framework.TemporaryContainers.ContainerCleanupScope.All) removes the containers of another test run that uses the same daemon right now. Keep it for
a daemon nothing else uses, such as a dedicated build agent.

## Remove the containers as soon as the process dies

A cleanup only runs when a later run calls it. To have the containers removed right away when the process is killed,
start a reaper: a watchdog container that keeps a connection to your process and removes what the process created once
that connection is gone.

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_Reaper;format=md-fenced-code-block;dedent=true -->
```csharp
// Keep it alive for as long as the containers it watches.
await using var reaper = await ContainerRuntime.Docker.StartReaperAsync();
```
<!-- endref -->

- It is opt-in. It runs the [`testcontainers/ryuk`](https://github.com/testcontainers/moby-ryuk) image and mounts the
  socket of the daemon into it.
- It needs a docker-compatible socket: it works with the Docker Engine API, `docker` and `podman`, not with Apple's
  `container` or `wslc`, where [StartReaperAsync](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.StartReaperAsync) throws a `NotSupportedException`.
- Disposing it stops the watchdog without removing anything: a run that ends normally disposes its containers itself.
- A connection that drops while the process is alive (a laptop that went to sleep) is opened again, so the watchdog
  does not mistake it for the end of the process.
- Its port is published on the loopback address only, since it accepts removal requests from whoever connects.
- It never removes the resources that have a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId).

[ContainerReaperOptions](cref:Meziantou.Framework.TemporaryContainers.ContainerReaperOptions) configures it:

| Option | Default | Effect |
| --- | --- | --- |
| [Image](cref:Meziantou.Framework.TemporaryContainers.ContainerReaperOptions.Image) | `testcontainers/ryuk:0.14.0` | The image of the watchdog, for instance from a mirror of your registry. |
| [SocketPath](cref:Meziantou.Framework.TemporaryContainers.ContainerReaperOptions.SocketPath) | The socket the runtime reports | The socket of the daemon, as the daemon sees it. |
| [ConnectionTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerReaperOptions.ConnectionTimeout) | 1 minute | How long the watchdog waits for the process to connect before it removes the resources. |
| [ReconnectionTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerReaperOptions.ReconnectionTimeout) | 10 seconds | How long the watchdog waits after the process is gone before it removes the resources. |

The default socket is the one the runtime connects to on Linux (a rootless daemon included), the socket of the Podman
service, and `/var/run/docker.sock` inside the virtual machine of Docker Desktop, Colima and similar tools. Set
[SocketPath](cref:Meziantou.Framework.TemporaryContainers.ContainerReaperOptions.SocketPath) when the daemon has its socket elsewhere.
