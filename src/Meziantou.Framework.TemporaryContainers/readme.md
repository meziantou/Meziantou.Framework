# Meziantou.Framework.TemporaryContainers

Manage temporary containers for integration tests by driving a container runtime CLI, so no daemon SDK is required.

Supported runtimes (auto-detected, or set `ContainerDefinition.Runtime`): `docker`, `podman`, Apple's `container` (macOS), and `wslc` (Windows/WSL).

```c#
var definition = new ContainerDefinition(new RegistryImage("redis:8"));
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
var definition = new ContainerDefinition(new DockerfileImage("./Dockerfile", "."));
definition.Ports.Add(new ContainerPort(8080));
definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));

await using var container = definition.CreateContainer();
await container.StartAsync();
```

## Database helpers

`CreateRedis` and `CreatePostgreSql` return pre-configured definitions whose container exposes `GetConnectionString()`.

```c#
await using var redis = ContainerDefinition.CreateRedis().CreateContainer();
await redis.StartAsync();
var redisConnectionString = redis.GetConnectionString(); // 127.0.0.1:<port>

var definition = ContainerDefinition.CreatePostgreSql(); // or CreatePostgreSql(new RegistryImage("postgres:16"))
definition.Environment.Add("POSTGRES_DB", "mydb");
await using var postgres = definition.CreateContainer();
await postgres.StartAsync();
var postgresConnectionString = postgres.GetConnectionString(); // Host=127.0.0.1;Port=<port>;Username=postgres;Password=postgres;Database=mydb
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
