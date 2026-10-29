using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Samples;

// The examples of docs/20-TemporaryContainers. They start containers, which needs a container runtime and pulls images,
// so they are compiled but not run. The tests of TemporaryContainers cover the same calls.
public static class DocumentationExamples
{
    public static async Task GettingStarted()
    {
        #region TemporaryContainers_GettingStarted
        await using var container = ContainerDefinition.CreatePostgreSql().CreateContainer();
        await container.StartAsync(); // pulls the image if needed, then waits until PostgreSQL accepts connections

        var connectionString = container.GetConnectionString();
        // Host=127.0.0.1;Port=<random port>;Username=postgres;Password=<random password>;Database=postgres
        #endregion

        Console.WriteLine(connectionString);
    }

    public static async Task Redis()
    {
        #region TemporaryContainers_Redis
        await using var redis = ContainerDefinition.CreateRedis().CreateContainer();
        await redis.StartAsync();

        var connectionString = redis.GetConnectionString(); // 127.0.0.1:<port>, for StackExchange.Redis
        #endregion

        Console.WriteLine(connectionString);
    }

    public static async Task PostgreSql()
    {
        #region TemporaryContainers_PostgreSql
        var definition = ContainerDefinition.CreatePostgreSql(ImageSource.FromRegistry("postgres:17"));
        definition.Environment.Add("POSTGRES_USER", "app");
        definition.Environment.Add("POSTGRES_DB", "orders");

        await using var postgres = definition.CreateContainer();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString(); // Host=127.0.0.1;Port=<port>;Username=app;Password=<password>;Database=orders
        #endregion

        Console.WriteLine(connectionString);
    }

    public static async Task MongoDb()
    {
        #region TemporaryContainers_MongoDb
        var definition = ContainerDefinition.CreateMongoDb();
        definition.RootUsername = "admin"; // "root" by default; the password is generated

        await using var mongo = definition.CreateContainer();
        await mongo.StartAsync();

        var connectionString = mongo.GetConnectionString(enableJournaling: true); // mongodb://admin:<password>@127.0.0.1:<port>/?authSource=admin&j=true
        #endregion

        Console.WriteLine(connectionString);
    }

    public static async Task SqlServer()
    {
        #region TemporaryContainers_SqlServer
        await using var sqlServer = ContainerDefinition.CreateSqlServer().CreateContainer();
        await sqlServer.StartAsync();

        var connectionString = sqlServer.GetConnectionString(); // Server=127.0.0.1,<port>;Database=master;User Id=sa;Pwd=<password>;...
        #endregion

        Console.WriteLine(connectionString);
    }

    public static ContainerDefinition[] ImageSources()
    {
        #region TemporaryContainers_ImageSources
        var fromRegistry = new ContainerDefinition(ImageSource.FromRegistry("nginx:1.29")); // a name with a tag or a digest
        var fromString = new ContainerDefinition("nginx:1.29"); // a string converts to a registry image
        var fromDockerfile = new ContainerDefinition(ImageSource.FromDockerfile("src/MyApp/Dockerfile", contextDirectory: "src"));
        var fromArchive = new ContainerDefinition(ImageSource.FromArchive("artifacts/my-app.tar")); // produced by 'docker save'
        var fromExisting = new ContainerDefinition(ImageSource.FromExisting("my-app:dev")); // an image already on the machine
        #endregion

        return [fromRegistry, fromString, fromDockerfile, fromArchive, fromExisting];
    }

    public static void PullPolicyExample(ContainerDefinition definition)
    {
        #region TemporaryContainers_PullPolicy
        definition.PullPolicy = PullPolicy.Always; // pull a moving tag such as 'latest' on every run
        #endregion
    }

    public static ContainerDefinition CommandAndEnvironment()
    {
        #region TemporaryContainers_CommandAndEnvironment
        var definition = new ContainerDefinition(ImageSource.FromRegistry("alpine:3.22"));
        definition.Entrypoint.Add("/bin/sh"); // replaces the entrypoint of the image
        definition.Command.AddRange(["-c", "echo started && sleep 3600"]);
        definition.Environment.Add("APP_MODE", "test"); // adds or replaces a variable
        definition.User = "1000:1000";
        definition.WorkingDirectory = "/tmp";
        definition.Hostname = "test-server";
        definition.Labels.Add("com.example.suite", "integration");
        #endregion

        return definition;
    }

    public static void Resources(ContainerDefinition definition)
    {
        #region TemporaryContainers_Resources
        definition.Resources.MemoryLimit = 512L * 1024 * 1024; // in bytes
        definition.Resources.CpuLimit = 1.5; // number of CPUs
        definition.Resources.ReadOnlyRootFilesystem = true;
        #endregion
    }

    public static void Network(ContainerDefinition definition)
    {
        #region TemporaryContainers_Network
        definition.Network.Network = "integration-tests"; // a network that already exists
        definition.Network.Alias = "database"; // the name the other containers of the network use
        #endregion
    }

    public static async Task Ports()
    {
        #region TemporaryContainers_Ports
        var definition = new ContainerDefinition(ImageSource.FromRegistry("nginx:1.29"));
        definition.Ports.Add(80); // published on a random free host port
        definition.Ports.Add(8443, 443); // host port 8443 to container port 443
        definition.Ports.Add(new ContainerPort(9000) with { HostIp = "0.0.0.0" }); // every interface, not only the loopback address

        await using var container = definition.CreateContainer();
        await container.StartAsync();

        var hostPort = container.GetMappedPort(80);
        var url = new UriBuilder("http", "127.0.0.1", hostPort).Uri;
        #endregion

        Console.WriteLine(url);
    }

    public static async Task VolumeOptions()
    {
        #region TemporaryContainers_VolumeOptions
        var volumeDefinition = new VolumeDefinition { Driver = "local" };
        volumeDefinition.DriverOptions.Add("type", "tmpfs");
        volumeDefinition.DriverOptions.Add("device", "tmpfs");
        volumeDefinition.Labels.Add("com.example.suite", "integration");

        await using var volume = volumeDefinition.CreateVolume();
        #endregion
    }

    public static void WaitStrategies(ContainerDefinition definition)
    {
        #region TemporaryContainers_WaitStrategies
        definition.WaitStrategies.Add(Wait.ForLogMessage("Now listening on")); // a log line contains the text
        definition.WaitStrategies.Add(Wait.ForLogMessage(new Regex(@"ready in \d+ ms", RegexOptions.None, TimeSpan.FromSeconds(1)), occurrences: 2));
        definition.WaitStrategies.Add(Wait.ForPort(8080)); // a TCP connection succeeds on the published port
        definition.WaitStrategies.Add(Wait.ForDelay(TimeSpan.FromSeconds(1)));
        definition.StartupTimeout = TimeSpan.FromMinutes(2); // 5 minutes by default
        #endregion
    }

    #region TemporaryContainers_CustomWaitStrategy
    public sealed class HttpHealthWaitStrategy(int containerPort, string path) : IWaitStrategy
    {
        public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
        {
            var url = new UriBuilder("http", "127.0.0.1", container.GetMappedPort(containerPort), path).Uri;
            using var httpClient = new HttpClient();
            while (true)
            {
                try
                {
                    using var response = await httpClient.GetAsync(url, cancellationToken);
                    if (response.IsSuccessStatusCode)
                        return;
                }
                catch (HttpRequestException)
                {
                    // Not listening yet
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }

        // Shown in the TimeoutException when the container is not ready in time
        public override string ToString() => "HTTP health check " + path;
    }
    #endregion

    public static void UseCustomWaitStrategy(ContainerDefinition definition)
    {
        #region TemporaryContainers_UseCustomWaitStrategy
        definition.Ports.Add(8080);
        definition.WaitStrategies.Add(new HttpHealthWaitStrategy(8080, "/health"));
        #endregion
    }

    public static async Task Exec(TemporaryContainer container)
    {
        #region TemporaryContainers_Exec
        var result = await container.ExecAsync(options =>
        {
            options.Command = ["psql", "--username", "postgres", "--tuples-only", "--command", "SELECT version()"];
            options.Environment.Add("PGCONNECT_TIMEOUT", "5");
        });

        // A command that fails does not throw: check its exit code
        if (result.ExitCode != 0)
            throw new InvalidOperationException("psql failed: " + result.StandardError);

        var version = result.StandardOutput.Trim();
        #endregion

        Console.WriteLine(version);
    }

    public static async Task ExecWithStandardInput(TemporaryContainer container)
    {
        #region TemporaryContainers_ExecStandardInput
        var result = await container.ExecAsync(options =>
        {
            options.Command = ["psql", "--username", "postgres"];
            options.StandardInput = InputSource.FromText("CREATE TABLE orders (id int PRIMARY KEY);");
        });
        #endregion

        Console.WriteLine(result.ExitCode);
    }

    public static async Task Files(TemporaryContainer container)
    {
        #region TemporaryContainers_Files
        using var settings = new MemoryStream("""{ "mode": "test" }"""u8.ToArray());
        await container.WriteFileAsync("/tmp/settings.json", settings); // creates or overwrites the file

        await container.CopyToContainerAsync("testdata", "/tmp/testdata"); // a file or a directory
        await container.CopyFromContainerAsync("/var/log", "artifacts/container-logs");

        await using var stream = await container.OpenReadAsync("/etc/os-release");
        using var reader = new StreamReader(stream);
        var osRelease = await reader.ReadToEndAsync();
        #endregion

        Console.WriteLine(osRelease);
    }

    public static async Task Logs(TemporaryContainer container)
    {
        #region TemporaryContainers_Logs
        // Follows the logs from the creation of the container until it stops: break or cancel to stop earlier
        await foreach (var entry in container.GetLogsAsync())
        {
            Console.WriteLine($"[{entry.Stream}] {entry.Message}");
            if (entry.Message.Contains("checkpoint complete", StringComparison.Ordinal))
                break;
        }
        #endregion
    }

    public static ContainerDefinition ForwardLogs(ILogger logger)
    {
        #region TemporaryContainers_ForwardLogs
        var definition = ContainerDefinition.CreateRedis();
        definition.Logging.Logger = logger; // standard output is logged as Information, standard error as Error
        definition.Logging.CaptureStandardOutput = false; // forward standard error only
        #endregion

        return definition;
    }

    public static async Task Lifecycle(TemporaryContainer container)
    {
        #region TemporaryContainers_Lifecycle
        await container.StopAsync();
        await container.StartAsync(); // starts it again and runs the wait strategies

        await container.RestartAsync();
        await container.WaitUntilReadyAsync(); // a restart does not run the wait strategies

        await container.PauseAsync();
        await container.UnpauseAsync();

        var info = await container.InspectAsync();
        Console.WriteLine($"{info.Name}: {info.State}");

        await container.KillAsync();
        await container.DeleteAsync(); // removes it now; a later StartAsync creates a new container
        #endregion
    }

    public static async Task CreateWithoutStarting()
    {
        #region TemporaryContainers_EnsureCreated
        await using var container = ContainerDefinition.CreateRedis().CreateContainer();
        await container.EnsureCreatedAsync(); // creates the container without starting it
        Console.WriteLine(container.Name);
        #endregion
    }

    public static ContainerDefinition SelectRuntime()
    {
        #region TemporaryContainers_SelectRuntime
        var definition = ContainerDefinition.CreateRedis();
        definition.Runtime = ContainerRuntime.Podman; // ContainerRuntime.Auto by default
        #endregion

        return definition;
    }

    public static async Task<ContainerRuntime> RuntimeFromEnvironment()
    {
        #region TemporaryContainers_RuntimeFromEnvironment
        var runtime = Environment.GetEnvironmentVariable("TEST_CONTAINER_RUNTIME") switch
        {
            "docker" => ContainerRuntime.Docker,
            "podman" => ContainerRuntime.Podman,
            _ => ContainerRuntime.Auto,
        };

        // Probes the runtime: runs a command or connects to the daemon
        if (!await runtime.IsSupportedAsync())
            throw new InvalidOperationException($"The container runtime '{runtime}' is not available.");
        #endregion

        return runtime;
    }

    public static async Task ErrorHandling(TemporaryContainer container)
    {
        #region TemporaryContainers_ErrorHandling
        try
        {
            await container.StartAsync();
        }
        catch (ContainerRuntimeException ex)
        {
            // The runtime refused an operation: a CLI command (Command, ExitCode, StandardError) or a Docker Engine API request (StatusCode)
            Console.WriteLine($"{ex.Runtime} failed: {ex.Command} {ex.StatusCode} {ex.StandardError}");
            throw;
        }
        catch (TimeoutException ex)
        {
            // Not ready within StartupTimeout: the message names the wait strategy, the state of the container and its last log lines
            Console.WriteLine(ex.Message);
            throw;
        }
        #endregion
    }
}
