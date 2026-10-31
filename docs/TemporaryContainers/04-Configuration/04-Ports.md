---
title: Ports
description: Publish the ports of a container on the host and find the host port each one was given.
---

A port of the container is reachable from your tests once it is published on the host. Publish it on a random host
port, and ask the container which one it got:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Ports;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = new ContainerDefinition(ImageSource.FromRegistry("nginx"));
definition.Ports.Add(80); // published on a random free host port
definition.Ports.Add(8443, 443); // host port 8443 to container port 443
definition.Ports.Add(new ContainerPort(9000) with { HostIp = "0.0.0.0" }); // every interface, not only the loopback address

await using var container = definition.CreateContainer();
await container.StartAsync();

var hostPort = container.GetMappedPort(80);
var url = new UriBuilder("http", "127.0.0.1", hostPort).Uri;
```
<!-- endref -->

- `Ports.Add(containerPort)` publishes the port on a random free host port. Prefer it: tests that run at the same time,
  or a service already running on the machine, cannot collide with it.
- `Ports.Add(hostPort, containerPort)` publishes the port on a fixed host port. The host port comes first.
- A container port is published on one host port only: adding it twice throws an `ArgumentException`. `Ports.Remove`
  removes it, for instance from a [database definition](../03-Databases.md) you want to publish differently.
- [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort) returns the host port of a published container port. It throws an `InvalidOperationException` before
  [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync), and for a port that is not published.

When a random host port is taken by another process between the creation of the container and its start, [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync)
creates a new container with other ports, unless the container has a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId).

## Listen on another address

Ports are published on the loopback address (`127.0.0.1`, the [ContainerPort.DefaultHostIp](cref:Meziantou.Framework.TemporaryContainers.ContainerPort.DefaultHostIp) constant): the container is
reachable from this machine only, so a test database is not exposed to the network. Set [HostIp](cref:Meziantou.Framework.TemporaryContainers.ContainerPort.HostIp) to publish a port on
another address:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_HostIp;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Ports.Add(new ContainerPort(8080) with { HostIp = "0.0.0.0" }); // every interface
```
<!-- endref -->

Windows containers cannot bind a specific address, and `wslc` publishes through the loopback forwarding of WSL, so the
address is not passed to them. Apple's `container` cannot publish a port on an IPv6 address.

## After a restart

[StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) and [RestartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.RestartAsync) read the port mapping again, since a container that starts again may get other host
ports. Call [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort) after them rather than keeping the port of an earlier start.
