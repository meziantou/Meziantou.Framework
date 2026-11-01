---
title: Container runtimes
description: The supported container runtimes, how one is detected automatically, how to choose one, and how they differ.
---

The library does not run containers itself: it drives a container runtime installed on the machine.

| Runtime | [ContainerRuntime](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime) | Talks to |
| --- | --- | --- |
| Docker Engine API | [ContainerRuntime.DockerApi](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.DockerApi) | The Docker daemon, over its socket or named pipe, without the `docker` CLI |
| Docker | [ContainerRuntime.Docker](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.Docker) | The `docker` CLI |
| Podman | [ContainerRuntime.Podman](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.Podman) | The `podman` CLI |
| Apple container | [ContainerRuntime.AppleContainer](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.AppleContainer) | Apple's `container` CLI (macOS) |
| WSL containers | [ContainerRuntime.Wslc](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.Wslc) | The `wslc` CLI (Windows) |

## Automatic detection

A definition uses [ContainerRuntime.Auto](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.Auto) unless you set another runtime. It uses the first runtime of this list that
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

When no runtime is available, [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) throws an `InvalidOperationException` that lists the runtimes it looked for.

On a machine that has several runtimes, the order matters: a Mac with both Apple's `container` and Docker Desktop
uses Apple's `container` as long as its service runs, and a Windows machine with `wslc` uses it. Choose the runtime
explicitly to use another one.

## Choose a runtime

Set [Runtime](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Runtime) on the definition:

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

[IsSupportedAsync](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.IsSupportedAsync) runs a command or opens a connection to the daemon. It caches a success, not a failure.
[VolumeDefinition.Runtime](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition.Runtime) chooses the runtime of a volume the same way.

## Connect to a Docker daemon

The `docker` and `podman` runtimes connect wherever their command line is configured to (contexts, `DOCKER_HOST`).
The Docker Engine API runtime finds the daemon the way the docker CLI does, and uses the first source that is set:

1. `DOCKER_HOST`: `unix://`, `npipe://`, `tcp://`, `http://` or `https://`.
2. The docker context named by `DOCKER_CONTEXT`.
3. The current context of `config.json` (`currentContext`, set by `docker context use`). The configuration directory is
   `DOCKER_CONFIG`, `~/.docker` by default. A context reached over TCP uses the TLS certificates stored with it, and
   honors `skip-tls-verify`.
4. Otherwise, or with the `default` context, the sockets of the usual distributions, in this order, and the
   `docker_engine` named pipe on Windows:

   | Socket | Daemon |
   | --- | --- |
   | `/var/run/docker.sock` | Docker Engine, Docker Desktop |
   | `~/.docker/run/docker.sock`, `~/.docker/desktop/docker.sock` (Linux) | Docker Desktop without the default socket |
   | `~/.colima/default/docker.sock`, `~/.colima/docker.sock` | Colima |
   | `~/.orbstack/run/docker.sock` | OrbStack |
   | `~/.rd/docker.sock` | Rancher Desktop |
   | `$XDG_RUNTIME_DIR/docker.sock` (`/run/user/$UID` on Linux) | Rootless Docker |
   | `$XDG_RUNTIME_DIR/podman/podman.sock` | Podman |

A `DOCKER_HOST` or a context that cannot be used (a missing context, an `ssh://` address) is not replaced by another
daemon: the runtime is unavailable, as the docker CLI would fail.

For `DOCKER_HOST` with `tcp://` or `https://`, TLS follows the environment variables of the docker CLI:

- `DOCKER_TLS_VERIFY` turns on TLS and verifies the daemon certificate against the authorities of `ca.pem`. The file
  can hold several certificates, such as an intermediate authority and its root.
- `DOCKER_TLS` alone turns on TLS without verifying the daemon certificate.
- The `ca.pem`, `cert.pem` and `key.pem` files are read from `DOCKER_CERT_PATH`, the configuration directory by default.
  `cert.pem` and `key.pem` are the client certificate, when the daemon requires one.

When no daemon answers, the "not available" exception lists each endpoint the runtime tried and why it failed. The
[Logger](cref:Meziantou.Framework.TemporaryContainers.ContainerLoggingOptions.Logger) of a container logs, at the `Debug`
level, the endpoint the Docker Engine API uses, or why the Auto runtime did not use it.

## Runtime differences

| | Docker Engine API, Docker | Podman | Apple container | wslc |
| --- | --- | --- | --- | --- |
| Volumes ([TemporaryVolume](cref:Meziantou.Framework.TemporaryContainers.TemporaryVolume)) | Yes | Yes | Yes, no driver | No |
| Pause, restart | Yes | Yes | No (a restart is a stop and a start) | Pause only (a restart is a stop and a start) |
| [Hostname](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Hostname), `Network.Alias` | Yes | Yes | No | Yes |
| [HostIp](cref:Meziantou.Framework.TemporaryContainers.ContainerPort.HostIp) of a port | Yes, except Windows containers | Yes | Yes, IPv4 only | No, loopback forwarding of WSL |
| [PullPolicy.Never](cref:Meziantou.Framework.TemporaryContainers.PullPolicy.Never) | Yes | Yes | Yes | No |
| Built images removed and cleaned up | Yes | Yes | Yes | No, they are not labeled |
| Anonymous volumes removed with the container | Yes | Yes | Yes | No |
| [Reaper](07-Cleanup.md#remove-the-containers-as-soon-as-the-process-dies) | Yes | Yes | No | No |

The Docker Engine API cannot send [ExecOptions.StandardInput](cref:Meziantou.Framework.TemporaryContainers.ExecOptions.StandardInput) to a daemon reached over a named pipe, the default on
Windows: use the `docker` runtime there.

Every runtime reports a failed operation with a [ContainerRuntimeException](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntimeException). See [Troubleshooting](10-Troubleshooting.md).
