using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class ContainerDefinitionTests
{
    [Fact]
    public void ImageSource_FromRegistry_CreatesRegistryImage()
    {
        var image = ImageSource.FromRegistry("redis:8");

        var registryImage = Assert.IsType<RegistryImage>(image);
        Assert.Equal("redis:8", registryImage.Name);
    }

    [Fact]
    public void ImageSource_FromDockerfile_CreatesDockerfileImage1()
    {
        var image = ImageSource.FromDockerfile("/tmp/Dockerfile", "/tmp");

        var dockerfileImage = Assert.IsType<DockerfileImage>(image);
        Assert.Equal(Path.GetFullPath("/tmp/Dockerfile"), dockerfileImage.DockerfilePath);
        Assert.Equal(Path.GetFullPath("/tmp"), dockerfileImage.ContextDirectory);
    }

    [Fact]
    public void ImageSource_FromDockerfile_CreatesDockerfileImage2()
    {
        var image = ImageSource.FromDockerfile("/tmp/Dockerfile");

        var dockerfileImage = Assert.IsType<DockerfileImage>(image);
        Assert.Equal(Path.GetFullPath("/tmp/Dockerfile"), dockerfileImage.DockerfilePath);
        Assert.Equal(Path.GetFullPath("/tmp"), dockerfileImage.ContextDirectory);
    }

    [Fact]
    public void ImageSource_FromArchive_CreatesArchiveImage()
    {
        var image = ImageSource.FromArchive("/tmp/image.tar");

        var archiveImage = Assert.IsType<ArchiveImage>(image);
        Assert.Equal(Path.GetFullPath("/tmp/image.tar"), archiveImage.ArchivePath);
    }

    [Fact]
    public void ImageSource_FromExisting_CreatesExistingImage()
    {
        var image = ImageSource.FromExisting("sha256:abcd");

        var existingImage = Assert.IsType<ExistingImage>(image);
        Assert.Equal("sha256:abcd", existingImage.ImageId);
    }

    [Fact]
    public async Task CreateContainer_DeepClonesDefinition()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Environment.Add("A", "1");
        definition.Ports.Add(6379);

        await using var container = definition.CreateContainer();

        definition.Environment.Add("B", "2");
        definition.Ports.Add(1234);

        Assert.Equal(1, container.Definition.Environment.Count);
        Assert.True(container.Definition.Environment.Contains("A"));
        Assert.False(container.Definition.Environment.Contains("B"));
        Assert.Equal(1, container.Definition.Ports.Count);
    }

    [Fact]
    public void CopyConstructor_IsolatesCollectionsFromOriginal()
    {
        var original = new ContainerDefinition(new RegistryImage("redis:8"));
        original.Labels.Add("a", "1");

        var copy = new ContainerDefinition(original);
        original.Labels.Add("b", "2");

        Assert.True(copy.Labels.Contains("a"));
        Assert.False(copy.Labels.Contains("b"));
    }

    [Fact]
    public void CopyConstructor_CopiesScalarProperties()
    {
        var original = new ContainerDefinition(new RegistryImage("redis:8"))
        {
            Runtime = ContainerRuntime.Podman,
            PullPolicy = PullPolicy.Always,
            Name = "name",
            ReuseId = "reuse",
            Hostname = "host",
            User = "user",
            WorkingDirectory = "/app",
            StartupTimeout = TimeSpan.FromSeconds(42),
        };

        var copy = new ContainerDefinition(original);

        Assert.Equal(ContainerRuntime.Podman, copy.Runtime);
        Assert.Equal(PullPolicy.Always, copy.PullPolicy);
        Assert.Equal("name", copy.Name);
        Assert.Equal("reuse", copy.ReuseId);
        Assert.Equal("host", copy.Hostname);
        Assert.Equal("user", copy.User);
        Assert.Equal("/app", copy.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(42), copy.StartupTimeout);
    }

    [Fact]
    public async Task StartAsync_ForwardsContainerLogsToLogger()
    {
        var runtime = new InMemoryRuntime();
        var logger = new ListLogger();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
        };
        definition.Logging.Logger = logger;

        await using var container = definition.CreateContainer();
        await container.StartAsync();

        runtime.Emit(LogStream.Stdout, "hello from stdout");
        runtime.Emit(LogStream.Stderr, "hello from stderr");

        await logger.WaitForMessageAsync("hello from stdout");
        await logger.WaitForMessageAsync("hello from stderr");
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithRunningReusableContainer_ForwardsContainerLogsToLogger()
    {
        var runtime = new InMemoryRuntime
        {
            State = ContainerState.Running,
        };
        var logger = new ListLogger();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
            ReuseId = "reuse",
        };
        definition.Logging.Logger = logger;

        await using var container = definition.CreateContainer();
        await container.EnsureCreatedAsync();

        runtime.Emit(LogStream.Stdout, "attached");
        await logger.WaitForMessageAsync("attached");
    }

    [Fact]
    public async Task StopAsync_StopsForwardingContainerLogs()
    {
        var runtime = new InMemoryRuntime();
        var logger = new ListLogger();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
        };
        definition.Logging.Logger = logger;

        await using var container = definition.CreateContainer();
        await container.StartAsync();

        runtime.Emit(LogStream.Stdout, "before-stop");
        await logger.WaitForMessageAsync("before-stop");

        await container.StopAsync();

        runtime.Emit(LogStream.Stdout, "after-stop");
        await Task.Delay(200);
        Assert.DoesNotContain(logger.Messages, message => string.Equals(message, "after-stop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_ReattachesWhenTheLogStreamEndsWhileTheContainerIsRunning()
    {
        var runtime = new InMemoryRuntime();
        var logger = new ListLogger();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
        };
        definition.Logging.Logger = logger;

        await using var container = definition.CreateContainer();
        await container.StartAsync();

        runtime.Emit(LogStream.Stdout, "before-the-stream-ended");
        await logger.WaitForMessageAsync("before-the-stream-ended");

        // The runtimes end the log stream on their own while the container is running. Stopping there leaves the
        // container silent for the rest of its life.
        runtime.EndLogStream();
        runtime.Emit(LogStream.Stdout, "after-the-stream-ended");
        await logger.WaitForMessageAsync("after-the-stream-ended");

        // Attaching replays the log from the beginning, so what was already forwarded must not be logged twice.
        Assert.Equal(1, logger.Messages.Count(message => string.Equals(message, "before-the-stream-ended", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StartAsync_StopsForwardingWhenTheLogStreamEndsAndTheContainerIsNoLongerRunning()
    {
        var runtime = new InMemoryRuntime();
        var logger = new ListLogger();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
        };
        definition.Logging.Logger = logger;

        await using var container = definition.CreateContainer();
        await container.StartAsync();

        runtime.Emit(LogStream.Stdout, "before-the-container-exited");
        await logger.WaitForMessageAsync("before-the-container-exited");

        runtime.State = ContainerState.Exited;
        runtime.EndLogStream();
        runtime.Emit(LogStream.Stdout, "after-the-container-exited");
        await Task.Delay(500);

        Assert.DoesNotContain(logger.Messages, message => string.Equals(message, "after-the-container-exited", StringComparison.Ordinal));
        Assert.Equal(1, runtime.LogAttachCount);
    }

    private sealed class InMemoryRuntime : ContainerRuntime
    {
        private readonly List<LogEntry> _logs = [];
        private int _logStreamGeneration;
        private int _logAttachCount;

        public InMemoryRuntime()
            : base("InMemory")
        {
        }

        public ContainerState State { get; set; } = ContainerState.Created;

        /// <summary>Gets the number of times the log stream has been attached to.</summary>
        public int LogAttachCount => Volatile.Read(ref _logAttachCount);

        public override Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        internal override Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
        {
            return Task.FromResult("id");
        }

        internal override Task StartAsync(string id, CancellationToken cancellationToken)
        {
            State = ContainerState.Running;
            return Task.CompletedTask;
        }

        internal override Task StopAsync(string id, CancellationToken cancellationToken)
        {
            State = ContainerState.Exited;
            return Task.CompletedTask;
        }

        internal override Task DeleteAsync(string id, CancellationToken cancellationToken)
        {
            State = ContainerState.Removed;
            return Task.CompletedTask;
        }

        internal override Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
        {
            var info = new ContainerInfo
            {
                Id = id,
                Name = "name",
                State = State,
            };

            return Task.FromResult(info);
        }

        internal override async IAsyncEnumerable<LogEntry> GetLogsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Mimics the real runtimes: every attach replays the log from the beginning, and the stream can end on
            // its own while the container is still running.
            Interlocked.Increment(ref _logAttachCount);
            var generation = Volatile.Read(ref _logStreamGeneration);
            var index = 0;
            while (Volatile.Read(ref _logStreamGeneration) == generation)
            {
                LogEntry? entry = null;
                lock (_logs)
                {
                    if (index < _logs.Count)
                    {
                        entry = _logs[index];
                        index++;
                    }
                }

                if (entry is null)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                yield return entry;
            }
        }

        internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
        {
            return new Dictionary<int, int>();
        }

        public void Emit(LogStream stream, string message)
        {
            lock (_logs)
            {
                _logs.Add(new LogEntry(stream, message, Timestamp: null));
            }
        }

        /// <summary>Ends the current log stream, as the runtimes do while the container is still running.</summary>
        public void EndLogStream()
        {
            Interlocked.Increment(ref _logStreamGeneration);
        }
    }

    private sealed class ListLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Enqueue(formatter(state, exception));
        }

        public async Task WaitForMessageAsync(string message)
        {
            for (var i = 0; i < 100; i++)
            {
                if (_messages.Any(value => string.Equals(value, message, StringComparison.Ordinal)))
                    return;

                await Task.Delay(20);
            }

            throw new InvalidOperationException($"Message '{message}' was not forwarded to the logger.");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
