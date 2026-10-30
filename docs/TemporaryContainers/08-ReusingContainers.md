---
title: Reusing a container
description: Share one container between test processes and across runs with a reuse identifier, and remove it when you are done.
---

Starting a database can take longer than the tests that use it. A reuse identifier shares one container between the
processes that use the same identifier, and keeps it for the next run.

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_ReuseId;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = ContainerDefinition.CreatePostgreSql();
definition.ReuseId = "my-integration-tests";

await using var container = definition.CreateContainer();
await container.StartAsync(); // creates the container, or adopts the one another process created
```
<!-- endref -->

- The first process creates the container; the others adopt it.
- The container is not removed on dispose, so the next run adopts it too, already started.
- It is named after the reuse identifier, so two processes that start at the same time cannot both create one: the
  runtime rejects the second name, and that process adopts the container of the first one.
- An adopted container keeps the ports it was created with, so [GetMappedPort](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetMappedPort) returns the same host port in every
  process.

## The definition must not change

A container is only adopted by a definition equal to the one it was created from. A definition that changed (another
image tag, another environment variable) fails with an `InvalidOperationException` instead of being given the old
container. Change the reuse identifier along with the definition, or remove the old container.

The passwords the [database definitions](03-Databases.md) generate are the exception, since every process generates its
own: the adopting process reads back the ones the container was created with, so `GetConnectionString` works in every
process.

## Keep the data of the tests apart

A reused container keeps its data between runs, and the processes that share it see each other's data. Give each test
its own database, schema or key prefix, and do not assume the container starts empty.

## Remove reused containers

Nothing removes a reused container on its own: the default cleanup and the reaper leave it alone. Remove it when it is no
longer needed, for instance the ones that have been around for a day:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_CleanupReused;format=md-fenced-code-block;dedent=true -->
```csharp
await ContainerRuntime.Auto.CleanupAsync(new ContainerCleanupOptions
{
    Scope = ContainerCleanupScope.All,
    IncludeReusedResources = true,
    MinimumAge = TimeSpan.FromDays(1),
});
```
<!-- endref -->

Volumes work the same way: a [VolumeDefinition](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition) with a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.VolumeDefinition.ReuseId) is kept on dispose and adopted by the next run.
