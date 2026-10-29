---
title: Volumes and mounts
description: Mount host directories, volumes and tmpfs file systems, and create volumes that are removed after the test.
---

## Mount a directory, a volume or a tmpfs

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_Mounts;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
definition.Mounts.AddBindMount("/host/data", "/data", readOnly: true);
definition.Mounts.AddVolume("existing-volume", "/var/lib/redis");
definition.Mounts.AddTmpfs("/scratch");
```
<!-- endref -->

| Method | Mounts |
| --- | --- |
| `AddBindMount(hostPath, containerPath, readOnly)` | A file or directory of the host. Use it to give test data or configuration files to the container. |
| `AddVolume(volumeName, containerPath, readOnly)` | A volume of the runtime, by name. |
| `AddVolume(volume, containerPath, readOnly)` | A `TemporaryVolume`, created before the container (see below). |
| `AddTmpfs(containerPath)` | A file system in memory, empty at every start. Use it for scratch space, or with a read-only root file system. |

The `BindMount`, `VolumeMount` and `TmpfsMount` records describe the same mounts; pass one to `Mounts.Add`.

## Create a volume for the test

A `TemporaryVolume` creates a volume and removes it when disposed, so a test can keep data across two containers without
leaving anything behind:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_TemporaryVolume;format=md-fenced-code-block;dedent=true -->
```csharp
await using var volume = new VolumeDefinition().CreateVolume();

var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
definition.Mounts.AddVolume(volume, "/data"); // add `readOnly: true` for a read-only mount

await using var container = definition.CreateContainer();
await container.StartAsync(); // creates the volume if needed, then the container
```
<!-- endref -->

- `StartAsync` creates the volume when it does not exist yet, then the container, so the mount never resolves to an
  empty volume the runtime created on its own.
- Declare the volume before the containers that mount it, so it is disposed last: a runtime refuses to remove a volume
  that a container still references.
- Several containers can mount the same `TemporaryVolume` and start at the same time.

## Volume settings

`VolumeDefinition` sets the name, the driver and its options, and the labels of the volume:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_VolumeOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var volumeDefinition = new VolumeDefinition { Driver = "local" };
volumeDefinition.DriverOptions.Add("type", "tmpfs");
volumeDefinition.DriverOptions.Add("device", "tmpfs");
volumeDefinition.Labels.Add("com.example.suite", "integration");

await using var volume = volumeDefinition.CreateVolume();
```
<!-- endref -->

- Without a `Name`, the volume gets a random name.
- The volume is only removed when the library created it. A `Name` that already exists is adopted and left behind.
- A volume with a `ReuseId` is kept on dispose, so the next run reuses it. See
  [Reusing a container](../08-ReusingContainers.md).
- `CreateVolume` copies the definition: the volume owns a read-only copy, as containers do.

## Anonymous volumes

Some images declare volumes of their own (a `VOLUME` instruction in the Dockerfile). The runtime creates an anonymous
volume for each one, and the library removes them with the container, except with `wslc`, which has no option for it.

## Runtime differences

- `wslc` has no volume commands: a `TemporaryVolume` throws a `NotSupportedException` there.
- Apple's `container` has no volume driver, and its mount descriptors cannot contain a comma. As of version 1.1.0, it
  hangs on a container that mounts a volume a deleted container used, so keep a volume to a single container there.
