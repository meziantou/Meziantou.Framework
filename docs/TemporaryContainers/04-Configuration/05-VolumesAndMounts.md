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
| `AddVolume(volume, containerPath, readOnly)` | A [TemporaryVolume](cref:Meziantou.Framework.TemporaryContainers.TemporaryVolume), created before the container (see below). |
| `AddTmpfs(containerPath)` | A file system in memory, empty at every start. Use it for scratch space, or with a read-only root file system. |

A tmpfs mount takes no more than half of the memory of the host by default. Pass a size, in bytes, and the permissions
of its root to limit it:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_TmpfsOptions;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Mounts.AddTmpfs("/scratch", size: 64L * 1024 * 1024, mode: UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
```
<!-- endref -->

Apple's `container` does not take options for a tmpfs mount.

The [BindMount](cref:Meziantou.Framework.TemporaryContainers.BindMount), [VolumeMount](cref:Meziantou.Framework.TemporaryContainers.VolumeMount) and [TmpfsMount](cref:Meziantou.Framework.TemporaryContainers.TmpfsMount) records describe the same mounts; pass one to `Mounts.Add`.

## Create a volume for the test

A [TemporaryVolume](cref:Meziantou.Framework.TemporaryContainers.TemporaryVolume) creates a volume and removes it when disposed, so a test can keep data across two containers without
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

- [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) creates the volume when it does not exist yet, then the container, so the mount never resolves to an
  empty volume the runtime created on its own.
- Declare the volume before the containers that mount it, so it is disposed last: a runtime refuses to remove a volume
  that a container still references.
- Several containers can mount the same [TemporaryVolume](cref:Meziantou.Framework.TemporaryContainers.TemporaryVolume) and start at the same time.

## Volume settings

[VolumeDefinition](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition) sets the name, the driver and its options, and the labels of the volume:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_VolumeOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var volumeDefinition = new VolumeDefinition { Driver = "local" };
volumeDefinition.DriverOptions.Add("type", "tmpfs");
volumeDefinition.DriverOptions.Add("device", "tmpfs");
volumeDefinition.Labels.Add("com.example.suite", "integration");

await using var volume = volumeDefinition.CreateVolume();
```
<!-- endref -->

- Without a [Name](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition.Name), the volume gets a random name.
- The volume is only removed when the library created it. A [Name](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition.Name) that already exists is adopted and left behind.
- A volume with a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition.ReuseId) is kept on dispose, so the next run reuses it. See
  [Reusing a container](../08-ReusingContainers.md).
- [CreateVolume](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition.CreateVolume) copies the definition: the volume owns a read-only copy, as containers do.

## Anonymous volumes

Some images declare volumes of their own (a `VOLUME` instruction in the Dockerfile). The runtime creates an anonymous
volume for each one, and the library removes them with the container, except with `wslc`, which has no option for it.

## Runtime differences

- `wslc` has no volume commands: a [TemporaryVolume](cref:Meziantou.Framework.TemporaryContainers.TemporaryVolume) throws a `NotSupportedException` there.
- Apple's `container` has no volume driver, and its mount descriptors cannot contain a comma. As of version 1.1.0, it
  hangs on a container that mounts a volume a deleted container used, so keep a volume to a single container there.
