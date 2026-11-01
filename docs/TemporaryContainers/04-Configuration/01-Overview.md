---
title: Configuring a container
description: Describe a container with a ContainerDefinition, then create as many containers as you need from it.
---

A [ContainerDefinition](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition) describes a container: its image, its command, its environment, its ports, its mounts, and what
"ready" means. [CreateContainer](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.CreateContainer) turns it into a [TemporaryContainer](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer), which creates the container when you call
[StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) and removes it when you dispose it.

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/Examples.cs;region=ContainerDefinition_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
definition.Environment.Add("TZ", "UTC");
definition.Ports.Add(new ContainerPort(6379));
definition.WaitStrategies.Add(Wait.ForPort(6379));

await using var container = definition.CreateContainer();
await container.StartAsync();
```
<!-- endref -->

A few rules hold for every setting:

- **The container owns a copy.** [CreateContainer](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.CreateContainer) copies the definition, so you can change it and create another
  container from it without affecting the first one. The copy the container owns, `container.Definition`, is read-only:
  changing it throws an `InvalidOperationException`.
- **To start from an existing definition**, pass it to the copy constructor, `new ContainerDefinition(other)`. The copy
  can be changed even when `other` belongs to a container.
- **Names are generated.** A container with neither a [Name](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Name) nor a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) gets a random name, so two containers
  created from the same definition never collide, and a container whose creation failed can still be found and
  removed. Set [Name](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Name) only when something outside your code needs to find the container by name.
- **Defaults are safe.** Ports are published on the loopback address, and the runtime is detected automatically. See
  [Ports](04-Ports.md) and [Container runtimes](../06-Runtimes.md).

## Pages of this group

- [Images](02-Images.md): run an image from a registry, a Dockerfile, an archive or the local machine, and decide when
  it is pulled.
- [Command, environment and resources](03-CommandAndEnvironment.md): the entrypoint, the command, the environment
  variables, the user, the labels, the resource limits and the network.
- [Ports](04-Ports.md): publish the ports of the container and find the host port they were given.
- [Volumes and mounts](05-VolumesAndMounts.md): share files with the host or between containers.
- [Wait strategies](06-WaitStrategies.md): decide when [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) considers the container ready.
- [Networks](07-Networks.md): create a temporary network and connect containers to several networks, with aliases.
- [Advanced options](08-AdvancedOptions.md): capabilities, extra hosts, shared memory, ulimits, healthchecks, the stop
  signal, and the options the definition does not have.
