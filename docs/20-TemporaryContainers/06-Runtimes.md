---
title: Container runtimes
description: The supported container runtimes, how one is detected automatically, how to choose one, and how they differ.
---

The library does not run containers itself: it drives a container runtime installed on the machine.

| Runtime | `ContainerRuntime` | Talks to |
| --- | --- | --- |
| Docker Engine API | `ContainerRuntime.DockerApi` | The Docker daemon, over its socket or named pipe, without the `docker` CLI |
| Docker | `ContainerRuntime.Docker` | The `docker` CLI |
| Podman | `ContainerRuntime.Podman` | The `podman` CLI |
| Apple container | `ContainerRuntime.AppleContainer` | Apple's `container` CLI (macOS) |
| WSL containers | `ContainerRuntime.Wslc` | The `wslc` CLI (Windows) |

## Automatic detection

A definition uses `ContainerRuntime.Auto` unless you set another runtime. It uses the first runtime of this list that
is available:

1. `wslc`, on Windows.
1. Apple's `container`, on macOS.
1. The Docker Engine API.
1. The `docker` CLI.
1. The `podman` CLI.

A runtime is available when its command line is found on the `PATH` (the Docker Engine API needs none) and its daemon
answers. A daemon that is installed but stopped does not count: start Docker Desktop, the Podman machine, or Apple's
`container` service (`container system start`) before the tests. The first runtime that answers is kept for the rest of
the process; when none answers, the next attempt probes them again, so a daemon started later is still found.

When no runtime is available, `StartAsync` throws an `InvalidOperationException` that lists the runtimes it looked for.

On a machine that has several runtimes, the order matters: a Mac with both Apple's `container` and Docker Desktop
uses Apple's `container` as long as its service runs, and a Windows machine with `wslc` uses it. Choose the runtime
explicitly to use another one.

## Choose a runtime

Set `Runtime` on the definition:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_SelectRuntime;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = ContainerDefinition.CreateRedis();
definition.Runtime = ContainerRuntime.Podman; // ContainerRuntime.Auto by default
```
<!-- endref -->

To let each developer and build agent choose, read the runtime from the environment and check that it answers before
the tests:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_RuntimeFromEnvironment;format=md-fenced-code-block;dedent=true -->
```csharp
var runtime = Environment.GetEnvironmentVariable("TEST_CONTAINER_RUNTIME") switch
{
    "docker" => ContainerRuntime.Docker,
    "podman" => ContainerRuntime.Podman,
    _ => ContainerRuntime.Auto,
};

// Probes the runtime: runs a command or connects to the daemon
if (!await runtime.IsSupportedAsync())
    throw new InvalidOperationException($"The container runtime '{runtime}' is not available.");
```
<!-- endref -->

`IsSupportedAsync` runs a command or opens a connection to the daemon. It caches a success, not a failure.
`VolumeDefinition.Runtime` chooses the runtime of a volume the same way.

## Connect to a Docker daemon

The `docker` and `podman` runtimes connect wherever their command line is configured to (contexts, `DOCKER_HOST`).
The Docker Engine API runtime reads the same environment variables as the docker CLI:

- `DOCKER_HOST` selects the daemon: `unix://`, `npipe://`, `tcp://`, `http://` or `https://`.
- `DOCKER_TLS_VERIFY` or `DOCKER_TLS` turns on TLS for a daemon reached over TCP, with the `ca.pem`, `cert.pem` and
  `key.pem` files of the `DOCKER_CERT_PATH` directory (`~/.docker` by default).
- Without `DOCKER_HOST`, it uses `/var/run/docker.sock` on Linux and macOS, then the socket of a rootless daemon on Linux
  (`$XDG_RUNTIME_DIR/docker.sock`), and the `docker_engine` named pipe on Windows.

## Runtime differences

| | Docker Engine API, Docker | Podman | Apple container | wslc |
| --- | --- | --- | --- | --- |
| Volumes (`TemporaryVolume`) | Yes | Yes | Yes, no driver | No |
| Pause, restart | Yes | Yes | No (a restart is a stop and a start) | Pause only (a restart is a stop and a start) |
| `Hostname`, `Network.Alias` | Yes | Yes | No | Yes |
| `HostIp` of a port | Yes, except Windows containers | Yes | Yes, IPv4 only | No, loopback forwarding of WSL |
| `PullPolicy.Never` | Yes | Yes | Yes | No |
| Built images removed and cleaned up | Yes | Yes | Yes | No, they are not labeled |
| Anonymous volumes removed with the container | Yes | Yes | Yes | No |
| [Reaper](07-Cleanup.md#remove-the-containers-as-soon-as-the-process-dies) | Yes | Yes | No | No |

The Docker Engine API cannot send `ExecOptions.StandardInput` to a daemon reached over a named pipe, the default on
Windows: use the `docker` runtime there.

Every runtime reports a failed operation with a `ContainerRuntimeException`. See [Troubleshooting](10-Troubleshooting.md).
