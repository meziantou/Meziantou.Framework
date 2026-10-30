---
title: Getting started
description: Install the package, start a PostgreSQL container for a test, then run an image of your own.
---

## Prerequisites

- A container runtime on the machine: Docker, Podman, Apple's `container` (macOS) or `wslc` (Windows). Its daemon must
  be running; for instance, run `container system start` once for Apple's `container`. See
  [Container runtimes](06-Runtimes.md).

## Install the package

Add the `Meziantou.Framework.TemporaryContainers` package to your test project:

```bash
dotnet add package Meziantou.Framework.TemporaryContainers
```

The examples of this documentation omit `using Meziantou.Framework.TemporaryContainers;`.

## Start a database

Start a PostgreSQL server and get its connection string:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
await using var container = ContainerDefinition.CreatePostgreSql().CreateContainer();
await container.StartAsync(); // pulls the image if needed, then waits until PostgreSQL accepts connections

var connectionString = container.GetConnectionString();
// Host=127.0.0.1;Port=<random port>;Username=postgres;Password=<random password>;Database=postgres
```
<!-- endref -->

- `CreatePostgreSql` returns a definition that is already configured: the image, the port, a random password, and the
  checks that tell when PostgreSQL accepts connections.
- `CreateContainer` creates the object that manages the container; nothing runs yet.
- `StartAsync` pulls the image when it is not on the machine, creates and starts the container, and returns once the
  server is ready.
- The port of the container is published on a random free port of `127.0.0.1`, so tests that run at the same time do
  not collide. The connection string contains it.
- Disposing the container (`await using`) removes it.

The other ready-made definitions are described in [Databases](03-Databases.md).

## Run your own image

Any image works. Describe the container with a `ContainerDefinition`, then create and start it:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
definition.Environment.Add("ALLOW_EMPTY_PASSWORD", "yes");
definition.Ports.Add(new ContainerPort(6379));
definition.WaitStrategies.Add(Wait.ForPort(6379));

await using var container = definition.CreateContainer();
await container.StartAsync();

var hostPort = container.GetMappedPort(6379);
// connect to 127.0.0.1:hostPort
```
<!-- endref -->

- `Ports.Add` publishes a port of the container; `GetMappedPort` returns the host port it was given.
- `WaitStrategies.Add` tells `StartAsync` what "ready" means. Without one, `StartAsync` returns as soon as the container
  is started, which is usually before the service inside it accepts connections. See
  [Wait strategies](04-Configuration/06-WaitStrategies.md).
- `CreateContainer` copies the definition: changing `definition` afterward does not change the container.

## Next steps

- [Configuring a container](04-Configuration/01-Overview.md): images, environment, ports, volumes and wait strategies.
- [Using containers in tests and CI](09-TestFrameworksAndCI.md): start a container once for a test class or for the
  whole test run.
- [Cleaning up](07-Cleanup.md): what happens to the containers of a test run that is interrupted.
