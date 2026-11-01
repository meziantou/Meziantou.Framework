---
title: Networks
description: Create a temporary network, connect containers to several networks, and reach them by alias.
---

Containers on the same network reach each other by name, without publishing any port. Create a
[TemporaryNetwork](cref:Meziantou.Framework.TemporaryContainers.TemporaryNetwork), add it to the containers, and give each container the aliases the others use:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/Examples.cs;region=NetworkDefinition_Example;format=md-fenced-code-block;dedent=true -->
```csharp
await using var network = new NetworkDefinition().CreateNetwork();

var redisDefinition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
redisDefinition.Network.Add(network, "redis");
await using var redis = redisDefinition.CreateContainer();
await redis.StartAsync();

// The second container reaches the first one by its alias.
var clientDefinition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
clientDefinition.Network.Add(network);
clientDefinition.Command.Add("redis-cli");
clientDefinition.Command.Add("-h");
clientDefinition.Command.Add("redis");
clientDefinition.Command.Add("ping");
await using var client = clientDefinition.CreateContainer();
await client.StartAsync();
```
<!-- endref -->

- The network is created when the first container that joins it is created, and removed when you dispose it. Dispose it
  after the containers: a network a container still uses cannot be removed.
- Its name is random, so parallel test runs never share a network. Read it from [Name](cref:Meziantou.Framework.TemporaryContainers.TemporaryNetwork.Name).
- The network carries the labels of the library, so [CleanupAsync](cref:Meziantou.Framework.TemporaryContainers.ContainerRuntime.CleanupAsync) and the
  [reaper](../07-Cleanup.md#remove-the-containers-as-soon-as-the-process-dies) remove it when a test run never disposes it.

## Configure the network

A [NetworkDefinition](cref:Meziantou.Framework.TemporaryContainers.NetworkDefinition) describes the network, like a [VolumeDefinition](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition) describes a volume:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_TemporaryNetworkOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var networkDefinition = new NetworkDefinition
{
    Internal = true, // the containers reach each other, but not the internet
};
networkDefinition.Labels.Add("purpose", "tests");

await using var network = networkDefinition.CreateNetwork();
await network.EnsureCreatedAsync(); // optional: the first container that joins the network creates it
Console.WriteLine(network.Name); // a random name, removed when the network is disposed
```
<!-- endref -->

| Property | Description |
| --- | --- |
| [Name](cref:Meziantou.Framework.TemporaryContainers.NetworkDefinition.Name) | The name of the network. A random name when it is not set. |
| [Driver](cref:Meziantou.Framework.TemporaryContainers.NetworkDefinition.Driver) | The network driver. The runtime uses `bridge` when it is not set. |
| [Internal](cref:Meziantou.Framework.TemporaryContainers.NetworkDefinition.Internal) | Isolates the network from the outside: its containers reach each other, but not the internet. |
| [ReuseId](cref:Meziantou.Framework.TemporaryContainers.NetworkDefinition.ReuseId) | Reuses the network across runs, and keeps it on dispose. |
| [Labels](cref:Meziantou.Framework.TemporaryContainers.NetworkDefinition.Labels) | Labels added to the network. |

A network that already exists when the library would create it is adopted and left in place on dispose, so pointing a
definition at an existing network never removes it.

## Join several networks

A container can join several networks, with several aliases on each. Pass the name of an existing network, or a
[TemporaryNetwork](cref:Meziantou.Framework.TemporaryContainers.TemporaryNetwork):

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_SeveralNetworks;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Network.Add("frontend", "api"); // an existing network, and the aliases of the container on it
definition.Network.Add("backend", "api", "api-internal");
```
<!-- endref -->

The container is created on the first network and connected to the others before it starts, so it never runs without
them. [Network](cref:Meziantou.Framework.TemporaryContainers.ContainerNetworkOptions.Network) and [Alias](cref:Meziantou.Framework.TemporaryContainers.ContainerNetworkOptions.Alias) still work: the network they set comes first. Adding the same
network twice throws: pass all its aliases in one call.

## Runtime differences

- The Docker Engine API, Docker and Podman support everything on this page.
- Apple's `container` creates networks without a driver, and connects the container to all its networks when it creates it.
- `wslc` has no network commands: it cannot create a network or connect a container to more than one.
