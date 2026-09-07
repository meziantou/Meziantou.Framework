# Meziantou.Framework.TemporaryContainers

Manage temporary containers for integration tests by driving a container runtime CLI or the Docker Engine API, so no daemon SDK is required.

Supported runtimes (auto-detected, or set `ContainerDefinition.Runtime`): the Docker Engine API, `docker`, `podman`, Apple's `container` (macOS), and `wslc` (Windows/WSL).

```c#
var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
definition.Environment.Add("ALLOW_EMPTY_PASSWORD", "yes");
definition.Ports.Add(new ContainerPort(6379));
definition.WaitStrategies.Add(Wait.ForPort(6379));

await using var container = definition.CreateContainer();
await container.StartAsync();

var hostPort = container.GetMappedPort(6379);
// connect to 127.0.0.1:hostPort
```

`StartAsync` creates the container, starts it, and runs the registered wait strategies before returning.
The container is removed when disposed. Set `ContainerDefinition.ReuseId` to reuse an existing container across runs; reused containers are not removed on dispose.

## Building an image

```c#
var definition = new ContainerDefinition(ImageSource.FromDockerfile("./Dockerfile", "."));
definition.Ports.Add(new ContainerPort(8080));
definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));

await using var container = definition.CreateContainer();
await container.StartAsync();
```

## Volumes and mounts

```c#
var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
definition.Mounts.AddBindMount("/host/data", "/data", readOnly: true);
definition.Mounts.AddVolume("existing-volume", "/var/lib/redis");
definition.Mounts.AddTmpfs("/scratch");
```

A `TemporaryVolume` creates the volume and removes it when disposed, so a test can share data between two containers without leaving anything behind:

```c#
await using var volume = new VolumeDefinition().CreateVolume();

var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
definition.Mounts.AddVolume(volume, "/data"); // add `readOnly: true` for a read-only mount

await using var container = definition.CreateContainer();
await container.StartAsync(); // creates the volume if needed, then the container
```

Declare the volume before the containers that mount it, so it is disposed last: a runtime refuses to remove a volume a container still references.

The volume is only removed when the library created it. A `VolumeDefinition.Name` that already exists is adopted and left behind, and a volume with a `ReuseId` is kept so the next run can reuse it. Anonymous volumes declared by the image are removed with the container.

Runtime differences: `wslc` has no volume commands; Apple's `container` has no volume driver, its mount descriptors cannot contain a comma, and (as of 1.1.0) it hangs on a container that mounts a volume a deleted container used, so a volume there is best kept to a single container.

## Cleaning up leftovers

Every container and volume the library creates is labelled with the run that created it, so a later run can tell its own leftovers from the resources it must not touch. Disposal already removes them; the labels are what makes a run that was interrupted (a debugger stopped, a killed test host) recoverable.

```c#
// Removes the containers and volumes whose creating process is gone. Nothing else is touched:
// not the resources of a live process, not those created on another machine, not the reused ones.
var result = await ContainerRuntime.Auto.CleanupAsync();
Console.WriteLine($"{result.RemovedCount} leftovers removed");
```

Call it once when a test run starts (an xUnit assembly fixture, a `[ModuleInitializer]`, a script). `ContainerCleanupOptions` widens or narrows what it removes:

```c#
await ContainerRuntime.Docker.CleanupAsync(new ContainerCleanupOptions
{
    Scope = ContainerCleanupScope.All,          // every resource of this library, not only the orphaned ones
    MinimumAge = TimeSpan.FromHours(1),         // ... that is at least one hour old
    IncludeVolumes = false,                     // ... and only containers
    IncludeReusedResources = true,              // ... including the containers kept alive by a ReuseId
});
```

The resources of the current process are never removed, whatever the scope, so a cleanup at the beginning of a run cannot take down the containers that run is about to use. The runs of *other* processes are only spared by the default scope: `ContainerCleanupScope.All` removes the containers another run started against the same daemon while it is still using them, so keep it for a daemon nothing else is running against.

### Removing the containers as soon as the process dies

A cleanup only runs when a later run calls it. To have the leftovers removed right away, even when the process is killed, start a reaper: a watchdog container that holds a connection to this process and removes what the process created once that connection is gone.

```c#
// Keep it alive for as long as the containers it watches.
await using var reaper = await ContainerRuntime.Docker.StartReaperAsync();
```

It is opt-in: it starts a container of its own ([`testcontainers/ryuk`](https://github.com/testcontainers/moby-ryuk), configurable through `ContainerReaperOptions`) and mounts the daemon socket into it. It needs a docker-compatible socket, so it works with the Docker Engine API, `docker`, and `podman`, but not with Apple's `container` or `wslc`. Disposing it stops the watchdog without removing anything, so a run that ends normally disposes its containers itself.

The containers kept alive by a `ReuseId` are not part of a session: neither the reaper nor the default cleanup removes them.

## Sharing a container between test processes

Set the same `ReuseId` in every process. The first one creates the container, the others adopt it, and it is not removed on dispose:

```c#
var definition = ContainerDefinition.CreatePostgreSql();
definition.ReuseId = "my-integration-tests";

await using var container = definition.CreateContainer();
await container.StartAsync(); // creates the container, or adopts the one another process created
```

The container is named after the reuse identifier, so two processes that start at the same time cannot both create one: the runtime rejects the second name and that process adopts the container the first one created. An adopted container keeps the ports it was created with, so `GetMappedPort` reports the same host port in every process.

Nothing removes a reused container on its own. Remove it when it is no longer needed with `CleanupAsync` and `IncludeReusedResources`, for instance the ones that have been around for a day:

```c#
await ContainerRuntime.Auto.CleanupAsync(new ContainerCleanupOptions
{
    Scope = ContainerCleanupScope.All,
    IncludeReusedResources = true,
    MinimumAge = TimeSpan.FromDays(1),
});
```

## Database helpers

`CreateRedis`, `CreatePostgreSql`, `CreateMongoDb`, and `CreateSqlServer` return pre-configured definitions whose container exposes `GetConnectionString()`.

```c#
await using var redis = ContainerDefinition.CreateRedis().CreateContainer();
await redis.StartAsync();
var redisConnectionString = redis.GetConnectionString(); // 127.0.0.1:<port>

var postgresDefinition = ContainerDefinition.CreatePostgreSql(); // or CreatePostgreSql(ImageSource.FromRegistry("postgres:16"))
postgresDefinition.Environment.Add("POSTGRES_DB", "mydb");
await using var postgres = postgresDefinition.CreateContainer();
await postgres.StartAsync();
var postgresConnectionString = postgres.GetConnectionString(); // Host=127.0.0.1;Port=<port>;Username=postgres;******;Database=mydb

await using var mongo = ContainerDefinition.CreateMongoDb().CreateContainer();
await mongo.StartAsync();
var mongoConnectionString = mongo.GetConnectionString(); // mongodb://127.0.0.1:<port>

var sqlDefinition = ContainerDefinition.CreateSqlServer();
// Optional: override the generated strong random password
sqlDefinition.SaPassword = "Abcdef1!Abcdef1!";
await using var sqlServer = sqlDefinition.CreateContainer();
await sqlServer.StartAsync();
var sqlServerConnectionString = sqlServer.GetConnectionString(); // Server=127.0.0.1,<port>;Database=master;User Id=sa;Pwd=<password>;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30
```

## Interacting with a container

```c#
var result = await container.ExecAsync(options =>
{
    options.Command.Add("echo");
    options.Command.Add("hello");
});
await using var stream = await container.OpenReadAsync("/etc/hostname");
await foreach (var log in container.GetLogsAsync())
    Console.WriteLine(log.Message);
```
