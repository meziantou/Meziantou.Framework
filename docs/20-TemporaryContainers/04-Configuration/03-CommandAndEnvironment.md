---
title: Command, environment and resources
description: Set the entrypoint, the command, the environment variables, the user, the labels, the resource limits and the network of a container.
---

## Command and environment

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_CommandAndEnvironment;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = new ContainerDefinition(ImageSource.FromRegistry("alpine:3.22"));
definition.Entrypoint.Add("/bin/sh"); // replaces the entrypoint of the image
definition.Command.AddRange(["-c", "echo started && sleep 3600"]);
definition.Environment.Add("APP_MODE", "test"); // adds or replaces a variable
definition.User = "1000:1000";
definition.WorkingDirectory = "/tmp";
definition.Hostname = "test-server";
definition.Labels.Add("com.example.suite", "integration");
```
<!-- endref -->

| Setting | Effect |
| --- | --- |
| `Entrypoint` | Replaces the entrypoint of the image. Each item is one argument. |
| `Command` | The command passed to the entrypoint, one argument per item. |
| `Environment` | The environment variables. `Add` replaces a variable that is already set; `GetValue`, `Contains` and `Remove` read and remove them. |
| `User` | The user the container runs as (`name`, `uid` or `uid:gid`). |
| `WorkingDirectory` | The working directory inside the container. |
| `Hostname` | The hostname of the container. |
| `Labels` | Labels added to the container, for your own tools. |

Each argument is passed as is, without a shell: to use shell syntax (`&&`, variables), run a shell as in the example.

The values of the environment variables are passed to the command lines through a file only the current user can read,
not on their command line, which every user of the machine can read. `wslc` is the exception, and so is a value that
spans several lines, which stays on the command line.

## Resource limits

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Resources;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Resources.MemoryLimit = 512L * 1024 * 1024; // in bytes
definition.Resources.CpuLimit = 1.5; // number of CPUs
definition.Resources.ReadOnlyRootFilesystem = true;
```
<!-- endref -->

- `MemoryLimit` is in bytes, and `CpuLimit` is a number of CPUs, which can be fractional.
- `ReadOnlyRootFilesystem` makes the file system of the image read-only. Mount a [tmpfs](05-VolumesAndMounts.md) where
  the service needs to write.

## Network

By default, the container uses the default network of the runtime, and you reach it through its
[published ports](04-Ports.md). To have containers reach each other by name, connect them to the same network:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Network;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Network.Network = "integration-tests"; // a network that already exists
definition.Network.Alias = "database"; // the name the other containers of the network use
```
<!-- endref -->

The library does not create networks: create the network with the runtime first, for instance with
`docker network create integration-tests`.

## Runtime differences

Apple's `container` cannot set `Hostname` or `Network.Alias`. See [Container runtimes](../06-Runtimes.md) for the other
differences.
