using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Meziantou.Extensions.Logging.Xunit.v3;

namespace Meziantou.Framework.TemporaryContainers.Tests;

/// <summary>Integration tests shared by every runtime. One concrete subclass per runtime supplies the runtime to exercise; the whole class is skipped when that runtime is not installed.</summary>
public abstract class ContainerRuntimeTestsBase
{
    private const string BusyboxImage = "busybox:1.37";
    private const string HttpServerCommand = "mkdir -p /www; printf 'hello from container' > /www/index.html; echo SERVER READY; exec httpd -f -p 8080 -h /www";

    protected ContainerRuntimeTestsBase(ContainerRuntime runtime)
    {
        Runtime = runtime;
        global::Xunit.Assert.SkipUnless(ContainerRuntimeInfo.IsAvailable(runtime), $"The '{runtime}' container runtime is not available on this system.");
    }

    protected ContainerRuntime Runtime { get; }

    [Fact]
    public async Task StartAsync_ServesHttpAndReportsReady()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        var content = await GetIndexContentAsync(container);
        Assert.Equal("hello from container", content);

        var info = await container.InspectAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Running, info.State);
        Assert.True(await container.ExistsAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task DockerfileImage_BuildsAndServesHttp()
    {
        var imageDirectory = Path.Combine(AppContext.BaseDirectory, "TestImage");
        var definition = new ContainerDefinition(new DockerfileImage(Path.Combine(imageDirectory, "Dockerfile"), imageDirectory))
        {
            Runtime = Runtime,
        };
        AddHttpPortBinding(definition);
        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));
        definition.WaitStrategies.Add(Wait.ForPort(8080));
        definition.Logging.Logger = XUnitLogger.CreateLogger();


        await using var container = await StartWithRetryAsync(definition);

        var content = await GetIndexContentAsync(container);
        Assert.Equal("hello from container", content);
    }

    [Fact]
    public async Task ExecAsync_RunsCommandInContainer()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        var exec = await container.ExecAsync(options =>
        {
            options.Command.Add("cat");
            options.Command.Add("/www/index.html");
        }, XunitCancellationToken);

        Assert.Equal(0, exec.ExitCode);
        Assert.Contains("hello from container", exec.StandardOutput);
    }

    [Fact]
    public async Task GetLogsAsync_StreamsReadyMessage()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        var readyLineFound = false;
        using var logsCts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        logsCts.CancelAfter(TimeSpan.FromSeconds(30));
        await foreach (var log in container.GetLogsAsync(logsCts.Token))
        {
            if (log.Message.Contains("SERVER READY", StringComparison.Ordinal))
            {
                readyLineFound = true;
                break;
            }
        }

        Assert.True(readyLineFound);
    }

    [Fact]
    public async Task Files_OpenReadWriteAndCopy()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        await using (var stream = await container.OpenReadAsync("/www/index.html", XunitCancellationToken))
        using (var reader = new StreamReader(stream))
        {
            Assert.Equal("hello from container", await reader.ReadToEndAsync(XunitCancellationToken));
        }

        using (var payload = new MemoryStream(Encoding.UTF8.GetBytes("written content")))
        {
            await container.WriteFileAsync("/tmp/written.txt", payload, XunitCancellationToken);
        }

        var writtenExec = await container.ExecAsync(options =>
        {
            options.Command.Add("cat");
            options.Command.Add("/tmp/written.txt");
        }, XunitCancellationToken);
        Assert.Contains("written content", writtenExec.StandardOutput);

        var localFile = Path.Combine(Path.GetTempPath(), "MezTC-copy-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(localFile, "copied content", XunitCancellationToken);
        try
        {
            await container.CopyToContainerAsync(localFile, "/tmp/copied.txt", XunitCancellationToken);
            var copiedExec = await container.ExecAsync(options =>
            {
                options.Command.Add("cat");
                options.Command.Add("/tmp/copied.txt");
            }, XunitCancellationToken);
            Assert.Contains("copied content", copiedExec.StandardOutput);
        }
        finally
        {
            File.Delete(localFile);
        }

        var downloaded = Path.Combine(Path.GetTempPath(), "MezTC-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            await container.CopyFromContainerAsync("/www/index.html", downloaded, XunitCancellationToken);
            Assert.Equal("hello from container", await File.ReadAllTextAsync(downloaded, XunitCancellationToken));
        }
        finally
        {
            File.Delete(downloaded);
        }
    }

    [Fact]
    public async Task Lifecycle_RestartStopAndDelete()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        await container.RestartAsync(XunitCancellationToken);
        Assert.True(container.GetMappedPort(8080) > 0);

        await container.StopAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Exited, (await container.InspectAsync(XunitCancellationToken)).State);

        await container.DeleteAsync(XunitCancellationToken);
        Assert.False(await container.ExistsAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Reuse_AdoptsExistingContainer()
    {
        var reuseId = "meziantou-tc-test-" + Guid.NewGuid().ToString("N");
        string firstId;

        var firstDefinition = CreateHttpServerDefinition();
        firstDefinition.ReuseId = reuseId;
        await using (var first = await StartWithRetryAsync(firstDefinition))
        {
            firstId = first.Id;
        }

        try
        {
            var secondDefinition = CreateHttpServerDefinition();
            secondDefinition.ReuseId = reuseId;
            await using var second = secondDefinition.CreateContainer();
            await second.EnsureCreatedAsync(XunitCancellationToken);

            Assert.Equal(firstId, second.Id);
        }
        finally
        {
            var cleanupDefinition = CreateHttpServerDefinition();
            cleanupDefinition.ReuseId = reuseId;
            var cleanup = cleanupDefinition.CreateContainer();
            await cleanup.EnsureCreatedAsync(XunitCancellationToken);
            await cleanup.DeleteAsync(XunitCancellationToken);
            await cleanup.DisposeAsync();
        }
    }

    /// <summary>Shared pause/unpause assertion for runtimes that support it (called from the relevant subclasses).</summary>
    protected async Task AssertPauseUnpauseAsync()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        await container.PauseAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Paused, (await container.InspectAsync(XunitCancellationToken)).State);

        await container.UnpauseAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Running, (await container.InspectAsync(XunitCancellationToken)).State);
    }

    protected ContainerDefinition CreateHttpServerDefinition()
    {
        var definition = new ContainerDefinition(new RegistryImage(BusyboxImage))
        {
            Runtime = Runtime,
        };
        definition.Command.Add("sh");
        definition.Command.Add("-c");
        definition.Command.Add(HttpServerCommand);
        AddHttpPortBinding(definition);
        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));
        definition.WaitStrategies.Add(Wait.ForPort(8080));
        definition.Logging.Logger = XUnitLogger.CreateLogger();
        return definition;
    }

    private void AddHttpPortBinding(ContainerDefinition definition)
    {
        if (Runtime is ContainerRuntime.AppleContainer)
        {
            definition.Ports.Add(GetFreeTcpPort(), 8080);
        }
        else
        {
            definition.Ports.Add(8080);
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> GetStringWithRetryAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        const int MaxAttempts = 60;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.GetStringAsync(uri, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private async Task<string> GetIndexContentAsync(TemporaryContainer container)
    {
        if (Runtime is ContainerRuntime.AppleContainer)
        {
            var exec = await container.ExecAsync(options =>
            {
                options.Command.Add("sh");
                options.Command.Add("-c");
                options.Command.Add("wget -qO- http://127.0.0.1:8080/");
            }, XunitCancellationToken);

            Assert.Equal(0, exec.ExitCode);
            return exec.StandardOutput.Trim();
        }

        var port = container.GetMappedPort(8080);
        using var client = new HttpClient();
        return await GetStringWithRetryAsync(client, new Uri($"http://127.0.0.1:{port}/"), XunitCancellationToken);
    }

    protected static async Task<TemporaryContainer> StartWithRetryAsync(ContainerDefinition definition)
    {
        const int MaxRetries = 3;
        for (var i = 0; ; i++)
        {
            var container = definition.CreateContainer();
            try
            {
                await container.StartAsync(XunitCancellationToken);
                return container;
            }
            catch when (i < MaxRetries)
            {
                await container.DisposeAsync();
                await Task.Delay(1000, XunitCancellationToken);
            }
        }
    }
}
