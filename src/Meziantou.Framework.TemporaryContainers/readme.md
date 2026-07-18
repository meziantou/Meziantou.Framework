# Meziantou.Framework.TemporaryContainers

Manage temporary containers for integration tests by driving a container runtime CLI, so no daemon SDK is required.

Supported runtimes (auto-detected, or set `ContainerDefinition.Runtime`): `docker`, `podman`, Apple's `container` (macOS), and `wslc` (Windows/WSL).

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
