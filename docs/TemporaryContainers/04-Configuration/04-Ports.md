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

var host = container.GetHostAddress(80); // 127.0.0.1, or the host of a remote daemon
var hostPort = container.GetMappedPort(80);
var url = new UriBuilder("http", host, hostPort).Uri;
```
<!-- endref -->

- `Ports.Add(containerPort)` publishes the port on a random free host port. Prefer it: tests that run at the same time,
  or a service already running on the machine, cannot collide with it.
- `Ports.Add(hostPort, containerPort)` publishes the port on a fixed host port. The host port comes first.
- A container port is published on one host port only: adding it twice throws an `ArgumentException`. `Ports.Remove`
  removes it, for instance from a [database definition](../03-Databases.md) you want to publish differently.
- [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort) returns the host port of a published container port. It throws an `InvalidOperationException` before
  [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync), and for a port that is not published.
- [GetHostAddress](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetHostAddress) returns the address to connect to: `127.0.0.1` unless the port is published on another
  address or the [daemon runs on another machine](#connect-to-a-remote-daemon). It throws in the same cases as
  [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort).

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

## UDP and exposed ports

A port is published for TCP. Set [Protocol](cref:Meziantou.Framework.TemporaryContainers.ContainerPort.Protocol) to publish a UDP port, and add a port to
[ExposedPorts](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ExposedPorts) to expose it without publishing it to the host:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_UdpAndExposedPorts;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Ports.Add(new ContainerPort(53) { Protocol = ContainerPortProtocol.Udp }); // published, UDP
definition.ExposedPorts.Add(new ContainerExposedPort(9000)); // exposed, not published
```
<!-- endref -->

- A container port is published once: it cannot be published for both TCP and UDP.
  [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort) returns its host port whatever the protocol.
- An exposed port is metadata: the containers of a [network](07-Networks.md) reach every port of the container, exposed or
  not. Apple's `container` does not support exposing a port.
- [Wait.ForPort](cref:Meziantou.Framework.TemporaryContainers.Wait.ForPort) opens a TCP connection, so it cannot wait for a UDP port.

## Connect to a remote daemon

[GetHostAddress](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetHostAddress) tells where a published port is reached from this machine. The database
[connection strings](../03-Databases.md) and [Wait.ForPort](06-WaitStrategies.md) use it too.

| The port is published on | The daemon runs on this machine | The daemon runs on another machine |
| --- | --- | --- |
| A specific address, such as `192.168.1.10` | That address | That address |
| The loopback address (`127.0.0.1`, `::1`) | That address | The host of the daemon |
| Every interface (`0.0.0.0`, `::`) | `127.0.0.1` (`::1` for `::`) | The host of the daemon |

The daemon runs on another machine when it is reached over `tcp://`, `ssh://`, `http(s)://`, or a named pipe of another
server, whose host is not `localhost` or a loopback address:

- the `docker` runtime reads `DOCKER_HOST`, then the endpoint of the current context (`docker context inspect`);
- the `podman` runtime reads `CONTAINER_HOST`;
- the Docker Engine API runtime uses the daemon it connects to (see [Runtimes](../06-Runtimes.md#connect-to-a-docker-daemon)).

The address is returned as is, an IPv6 address without brackets: enclose it in brackets in a URI, or use `UriBuilder`.

A port published on the loopback address of a remote daemon is only reachable from that machine. Publish it on every
interface, keeping in mind that the service is then reachable from the network:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_RemoteDaemon;format=md-fenced-code-block;dedent=true -->
```csharp
// DOCKER_HOST=tcp://docker.example.com:2376 or ssh://user@docker.example.com
var definition = ContainerDefinition.CreatePostgreSql();
definition.Ports.Remove(5432);
definition.Ports.Add(new ContainerPort(5432) with { HostIp = "0.0.0.0" });

await using var postgres = definition.CreateContainer();
await postgres.StartAsync();

var host = postgres.GetHostAddress(5432); // docker.example.com
var connectionString = postgres.GetConnectionString(); // Host=docker.example.com;Port=<port>;...
```
<!-- endref -->

## After a restart

[StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) and [RestartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.RestartAsync) read the port mapping again, since a container that starts again may get other host
ports. Call [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort) after them rather than keeping the port of an earlier start.
