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
