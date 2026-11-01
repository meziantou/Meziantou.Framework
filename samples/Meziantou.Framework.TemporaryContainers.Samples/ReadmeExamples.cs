namespace Meziantou.Framework.TemporaryContainers.Samples;

// The examples of src/Meziantou.Framework.TemporaryContainers/readme.md. They start containers, which needs a container
// runtime and pulls images, so they are compiled but not run. The tests of TemporaryContainers cover the same calls.
public static class ReadmeExamples
{
    public static async Task Usage()
    {
        #region TemporaryContainers_Usage
        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
        definition.Environment.Add("ALLOW_EMPTY_PASSWORD", "yes");
        definition.Ports.Add(new ContainerPort(6379));
        definition.WaitStrategies.Add(Wait.ForPort(6379));

        await using var container = definition.CreateContainer();
        await container.StartAsync();

        var host = container.GetHostAddress(6379); // 127.0.0.1, unless the daemon runs on another machine
        var hostPort = container.GetMappedPort(6379);
        // connect to host:hostPort
        #endregion

        Console.WriteLine($"{host}:{hostPort}");
    }

    public static void PublishOnEveryInterface(ContainerDefinition definition)
    {
        #region TemporaryContainers_HostIp
        definition.Ports.Add(new ContainerPort(8080) with { HostIp = "0.0.0.0" }); // every interface
        #endregion
    }

    public static async Task BuildImage()
    {
        #region TemporaryContainers_BuildImage
        var definition = new ContainerDefinition(ImageSource.FromDockerfile("./Dockerfile", "."));
        definition.Ports.Add(new ContainerPort(8080));
        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));

        await using var container = definition.CreateContainer();
        await container.StartAsync();
        #endregion
    }

    public static ContainerDefinition Mounts()
    {
        #region TemporaryContainers_Mounts
        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
        definition.Mounts.AddBindMount("/host/data", "/data", readOnly: true);
        definition.Mounts.AddVolume("existing-volume", "/var/lib/redis");
        definition.Mounts.AddTmpfs("/scratch");
        #endregion

        return definition;
    }

    public static async Task TemporaryVolume()
    {
        #region TemporaryContainers_TemporaryVolume
        await using var volume = new VolumeDefinition().CreateVolume();

        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
        definition.Mounts.AddVolume(volume, "/data"); // add `readOnly: true` for a read-only mount

        await using var container = definition.CreateContainer();
        await container.StartAsync(); // creates the volume if needed, then the container
        #endregion
    }

    public static async Task Cleanup()
    {
        #region TemporaryContainers_Cleanup
        // Removes the containers, images and volumes whose creating process is gone. Nothing else is touched:
        // not the resources of a live process, not those created on another machine, not the reused ones.
        var result = await ContainerRuntime.Auto.CleanupAsync();
        Console.WriteLine($"{result.RemovedCount} leftovers removed");
        #endregion
    }

    public static async Task CleanupOptions()
    {
        #region TemporaryContainers_CleanupOptions
        await ContainerRuntime.Docker.CleanupAsync(new ContainerCleanupOptions
        {
            Scope = ContainerCleanupScope.All,          // every resource of this library, not only the orphaned ones
            MinimumAge = TimeSpan.FromHours(1),         // ... that is at least one hour old
            IncludeVolumes = false,                     // ... not the volumes
            IncludeImages = false,                      // ... nor the images built from a Dockerfile
            IncludeReusedResources = true,              // ... including the containers kept alive by a ReuseId
        });
        #endregion
    }

    public static async Task Reaper()
    {
        #region TemporaryContainers_Reaper
        // Keep it alive for as long as the containers it watches.
        await using var reaper = await ContainerRuntime.Docker.StartReaperAsync();
        #endregion
    }

    public static async Task ReuseId()
    {
        #region TemporaryContainers_ReuseId
        var definition = ContainerDefinition.CreatePostgreSql();
        definition.ReuseId = "my-integration-tests";

        await using var container = definition.CreateContainer();
        await container.StartAsync(); // creates the container, or adopts the one another process created
        #endregion
    }

    public static async Task CleanupReusedContainers()
    {
        #region TemporaryContainers_CleanupReused
        await ContainerRuntime.Auto.CleanupAsync(new ContainerCleanupOptions
        {
            Scope = ContainerCleanupScope.All,
            IncludeReusedResources = true,
            MinimumAge = TimeSpan.FromDays(1),
        });
        #endregion
    }

    public static async Task DatabaseHelpers()
    {
        #region TemporaryContainers_DatabaseHelpers
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
        #endregion

        Console.WriteLine(redisConnectionString);
        Console.WriteLine(postgresConnectionString);
        Console.WriteLine(mongoConnectionString);
        Console.WriteLine(sqlServerConnectionString);
    }

    public static async Task<ExecResult> Interact(TemporaryContainer container)
    {
        #region TemporaryContainers_Interact
        var result = await container.ExecAsync(options =>
        {
            options.Command.Add("echo");
            options.Command.Add("hello");
        });
        await using var stream = await container.OpenReadAsync("/etc/hostname");
        await foreach (var log in container.GetLogsAsync())
            Console.WriteLine(log.Message);
        #endregion

        return result;
    }
}
