---
title: Databases
description: Start Redis, PostgreSQL, MongoDB or SQL Server with a ready-made definition and get its connection string.
---

`ContainerDefinition` has a factory method for each supported database. The definition it returns is already
configured: the image, the published port, a strong random password where the database needs one, and the wait
strategies that tell when the server accepts connections. The container it creates has a `GetConnectionString()`
method.

| Factory method | Default image | Port | Container type |
| --- | --- | --- | --- |
| `ContainerDefinition.CreateRedis()` | `redis:8.10` | 6379 | `RedisContainer` |
| `ContainerDefinition.CreatePostgreSql()` | `postgres:18` | 5432 | `PostgreSqlContainer` |
| `ContainerDefinition.CreateMongoDb()` | `mongo:8` | 27017 | `MongoDbContainer` |
| `ContainerDefinition.CreateSqlServer()` | `mcr.microsoft.com/mssql/server:2025-latest` | 1433 | `SqlServerContainer` |

The connection strings point to `127.0.0.1` and the host port of the container, so call `GetConnectionString` after
`StartAsync`: before, it throws an `InvalidOperationException`.

## Redis

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Redis;format=md-fenced-code-block;dedent=true -->
```csharp
await using var redis = ContainerDefinition.CreateRedis().CreateContainer();
await redis.StartAsync();

var connectionString = redis.GetConnectionString(); // 127.0.0.1:<port>, for StackExchange.Redis
```
<!-- endref -->

The connection string is `host:port`, the format StackExchange.Redis expects. The server has no password.

## PostgreSQL

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_PostgreSql;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = ContainerDefinition.CreatePostgreSql(ImageSource.FromRegistry("postgres:17"));
definition.Environment.Add("POSTGRES_USER", "app");
definition.Environment.Add("POSTGRES_DB", "orders");

await using var postgres = definition.CreateContainer();
await postgres.StartAsync();

var connectionString = postgres.GetConnectionString(); // Host=127.0.0.1;Port=<port>;Username=app;Password=<password>;Database=orders
```
<!-- endref -->

- The connection string is in the Npgsql format. It reads the user, the password and the database from the
  `POSTGRES_USER`, `POSTGRES_PASSWORD` and `POSTGRES_DB` variables of the definition, the variables the image uses to
  initialize the server. The user defaults to `postgres` and the database to the name of the user.
- The password is generated; set `Password` to choose it.

## MongoDB

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_MongoDb;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = ContainerDefinition.CreateMongoDb();
definition.RootUsername = "admin"; // "root" by default; the password is generated

await using var mongo = definition.CreateContainer();
await mongo.StartAsync();

var connectionString = mongo.GetConnectionString(enableJournaling: true); // mongodb://admin:<password>@127.0.0.1:<port>/?authSource=admin&j=true
```
<!-- endref -->

- `RootUsername` and `RootPassword` set the `MONGO_INITDB_ROOT_USERNAME` and `MONGO_INITDB_ROOT_PASSWORD` variables.
  The user is `root` and the password is generated unless you set them.
- The connection string authenticates against the `admin` database. Journaling is off (`j=false`) unless you pass
  `enableJournaling: true`.

## SQL Server

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_SqlServer;format=md-fenced-code-block;dedent=true -->
```csharp
await using var sqlServer = ContainerDefinition.CreateSqlServer().CreateContainer();
await sqlServer.StartAsync();

var connectionString = sqlServer.GetConnectionString(); // Server=127.0.0.1,<port>;Database=master;User Id=sa;Pwd=<password>;...
```
<!-- endref -->

- The definition accepts the end-user license agreement of the image (`ACCEPT_EULA=Y`) and generates a password that
  meets the complexity rules of SQL Server. Set `SaPassword` to choose it.
- The connection string logs in as `sa` to the `master` database, with `Encrypt=True` and
  `TrustServerCertificate=True`, since the server uses a self-signed certificate.
- Microsoft publishes the SQL Server image for x64 only: on an arm64 machine, it only runs where the runtime emulates
  x64.

## Choose the image and the settings

Each factory method has an overload that takes the image, to pin another version. The definition is a regular
`ContainerDefinition`: add environment variables, mounts or wait strategies before you create the container. This
example shows all four databases, with the passwords set explicitly:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_DatabaseHelpers;format=md-fenced-code-block;dedent=true -->
```csharp
await using var redis = ContainerDefinition.CreateRedis().CreateContainer();
await redis.StartAsync();
var redisConnectionString = redis.GetConnectionString(); // 127.0.0.1:<port>

var postgresDefinition = ContainerDefinition.CreatePostgreSql(); // or CreatePostgreSql(ImageSource.FromRegistry("postgres:16"))
postgresDefinition.Environment.Add("POSTGRES_DB", "mydb");
// Optional: override the generated strong random password
postgresDefinition.Password = "my-password";
await using var postgres = postgresDefinition.CreateContainer();
await postgres.StartAsync();
var postgresConnectionString = postgres.GetConnectionString(); // Host=127.0.0.1;Port=<port>;Username=postgres;Password=<password>;Database=mydb

await using var mongo = ContainerDefinition.CreateMongoDb().CreateContainer();
await mongo.StartAsync();
var mongoConnectionString = mongo.GetConnectionString(); // mongodb://root:<password>@127.0.0.1:<port>/?authSource=admin&j=false

var sqlDefinition = ContainerDefinition.CreateSqlServer();
// Optional: override the generated strong random password
sqlDefinition.SaPassword = "Abcdef1!Abcdef1!";
await using var sqlServer = sqlDefinition.CreateContainer();
await sqlServer.StartAsync();
var sqlServerConnectionString = sqlServer.GetConnectionString(); // Server=127.0.0.1,<port>;Database=master;User Id=sa;Pwd=<password>;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30
```
<!-- endref -->

The connection strings are built with a connection string builder, so a password that contains `;` or `=` still works.

## Other databases and services

Any other service is a `ContainerDefinition` with its image, its port and a wait strategy. See
[Configuring a container](04-Configuration/01-Overview.md) and [Wait strategies](04-Configuration/06-WaitStrategies.md).
