using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meziantou.Framework.TemporaryContainers.Internals;
using Meziantou.Framework.TemporaryContainers.Strategies;
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
    public async Task CreateContainer_TheDefinitionOfTheContainerCannotBeChanged()
    {
        // The container reads its definition at every step of its life: a reuse identifier set once it runs would make
        // dispose leave the container behind.
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        await using var container = definition.CreateContainer();

        Assert.True(container.Definition.IsReadOnly);
        Assert.False(definition.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => container.Definition.ReuseId = "reuse");
        Assert.Throws<InvalidOperationException>(() => container.Definition.Environment.Add("A", "1"));
        Assert.Throws<InvalidOperationException>(() => container.Definition.Ports.Add(80));
        Assert.Throws<InvalidOperationException>(() => container.Definition.Mounts.AddTmpfs("/tmp"));
        Assert.Throws<InvalidOperationException>(() => container.Definition.WaitStrategies.Add(Wait.ForPort(80)));
        Assert.Throws<InvalidOperationException>(() => container.Definition.Logging.Logger = null);
        Assert.Throws<InvalidOperationException>(() => container.Definition.Resources.MemoryLimit = 1);
        Assert.Throws<InvalidOperationException>(() => container.Definition.Network.Alias = "alias");
        Assert.Throws<InvalidOperationException>(() => container.Definition.Command.Clear());

        // A copy of it is a new definition, which can be changed.
        var copy = new ContainerDefinition(container.Definition) { ReuseId = "reuse" };
        Assert.Equal("reuse", copy.ReuseId);
    }

    [Fact]
    public async Task CreateContainer_NamesTheContainerWhenNothingElseDoes()
    {
        await using var unnamed = new ContainerDefinition(new RegistryImage("redis:8")).CreateContainer();
        await using var named = new ContainerDefinition(new RegistryImage("redis:8")) { Name = "mine" }.CreateContainer();
        await using var reused = new ContainerDefinition(new RegistryImage("redis:8")) { ReuseId = "reuse" }.CreateContainer();

        Assert.StartsWith(ResourceNaming.Prefix, unnamed.Definition.Name);
        Assert.Equal("mine", named.Definition.Name);
        Assert.Null(reused.Definition.Name);

        // The generated name belongs to that container: a copy of its definition creates a container of its own.
        Assert.Null(new ContainerDefinition(unnamed.Definition).Name);
        Assert.Equal("mine", new ContainerDefinition(named.Definition).Name);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ContainerPort_RejectsAPortOutOfRange(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerPort(port));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerPort(port, 80));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerPort(80) with { Port = port });
    }

    [Fact]
    public void ContainerPort_IsPublishedOnTheLoopbackAddressByDefault()
    {
        Assert.Equal("127.0.0.1", new ContainerPort(80).HostIp);
        Assert.Equal("0.0.0.0", (new ContainerPort(80) with { HostIp = "0.0.0.0" }).HostIp);
        Assert.Throws<ArgumentException>(() => new ContainerPort(80) with { HostIp = "localhost" });
    }

    [Fact]
    public void Ports_RejectsAContainerPortPublishedTwice()
    {
        // The runtimes disagree on what a port published twice means, and GetMappedPort can only report one host port.
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Ports.Add(8080, 80);

        Assert.Throws<ArgumentException>(() => definition.Ports.Add(80));
        Assert.True(definition.Ports.Remove(80));
        definition.Ports.Add(80);
        Assert.Equal(1, definition.Ports.Count);
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
        await container.StartAsync(XunitCancellationToken);

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
        await container.EnsureCreatedAsync(XunitCancellationToken);

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
        await container.StartAsync(XunitCancellationToken);

        runtime.Emit(LogStream.Stdout, "before-stop");
        await logger.WaitForMessageAsync("before-stop");

        await container.StopAsync(XunitCancellationToken);

        runtime.Emit(LogStream.Stdout, "after-stop");
        Assert.Equal(1, runtime.LogAttachCount);
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
        await container.StartAsync(XunitCancellationToken);

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
        await container.StartAsync(XunitCancellationToken);

        runtime.Emit(LogStream.Stdout, "before-the-container-exited");
        await logger.WaitForMessageAsync("before-the-container-exited");

        runtime.State = ContainerState.Exited;
        runtime.EndLogStream();
        await runtime.WaitForInspectionAsync();

        runtime.Emit(LogStream.Stdout, "after-the-container-exited");
        Assert.Equal(1, runtime.LogAttachCount);
        Assert.DoesNotContain(logger.Messages, message => string.Equals(message, "after-the-container-exited", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_ForwardsTheLogsOfAContainerThatExitedAndIsStartedAgain()
    {
        // The pump of the first run ends with the container. The second run must get a pump of its own.
        var runtime = new InMemoryRuntime();
        var logger = new ListLogger();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
        };
        definition.Logging.Logger = logger;

        await using var container = definition.CreateContainer();
        await container.StartAsync(XunitCancellationToken);
        runtime.Emit(LogStream.Stdout, "first run");
        await logger.WaitForMessageAsync("first run");

        runtime.State = ContainerState.Exited;
        runtime.EndLogStream();
        await runtime.WaitForInspectionAsync();

        await container.StartAsync(XunitCancellationToken);
        runtime.Emit(LogStream.Stdout, "second run");
        await logger.WaitForMessageAsync("second run");
    }

    [Fact]
    public async Task RestartAsync_DoesNotForwardTheLogsOfTheEarlierRunAgain()
    {
        var runtime = new InMemoryRuntime();
        var logger = new ListLogger();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
        };
        definition.Logging.Logger = logger;

        await using var container = definition.CreateContainer();
        await container.StartAsync(XunitCancellationToken);
        runtime.Emit(LogStream.Stdout, "before-restart");
        await logger.WaitForMessageAsync("before-restart");

        await container.RestartAsync(XunitCancellationToken);
        runtime.Emit(LogStream.Stdout, "after-restart");
        await logger.WaitForMessageAsync("after-restart");

        Assert.Equal(1, logger.Messages.Count(message => string.Equals(message, "before-restart", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_TheWaitDoesNotMatchTheLogsOfAnEarlierRun(bool logsIncludeTimestamps)
    {
        // A container started again keeps the logs of its earlier run. The ready message of that run must not report
        // the new run as ready, whether the runtime time-stamps its entries or not.
        var runtime = new InMemoryRuntime { LogsHaveTimestamps = logsIncludeTimestamps };
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
            StartupTimeout = TimeSpan.FromMinutes(5),
        };
        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));

        await using var container = definition.CreateContainer();
        runtime.OnStart = () => runtime.Emit(LogStream.Stdout, "SERVER READY");
        await container.StartAsync(XunitCancellationToken);
        await container.StopAsync(XunitCancellationToken);

        runtime.OnStart = null;
        var attachCount = runtime.LogAttachCount;
        var start = container.StartAsync(XunitCancellationToken);
        await runtime.WaitForLogAttachAsync(attachCount);

        // The wait is attached and has been replayed every entry: a match on the earlier run would complete it at once.
        Assert.NotSame(start, await Task.WhenAny(start, Task.Delay(TimeSpan.FromMilliseconds(200), XunitCancellationToken)));

        runtime.Emit(LogStream.Stdout, "SERVER READY");
        await start;
    }

    [Fact]
    public async Task DeleteAsync_ALaterStartCreatesANewContainer()
    {
        var runtime = new InMemoryRuntime();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test")) { Runtime = runtime };

        await using var container = definition.CreateContainer();
        await container.StartAsync(XunitCancellationToken);
        await container.DeleteAsync(XunitCancellationToken);

        Assert.Throws<InvalidOperationException>(() => container.Id);
        Assert.False(await container.ExistsAsync(XunitCancellationToken));

        await container.StartAsync(XunitCancellationToken);
        Assert.Equal(2, runtime.CreateCount);
    }

    [Fact]
    public async Task EnsureCreatedAsync_DoesNotCreateASecondContainerAfterAFailedInspect()
    {
        var runtime = new InMemoryRuntime { FailNextInspect = true };
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test")) { Runtime = runtime };

        await using var container = definition.CreateContainer();
        await Assert.ThrowsAsync<InvalidOperationException>(() => container.EnsureCreatedAsync(XunitCancellationToken));
        await container.EnsureCreatedAsync(XunitCancellationToken);

        Assert.Equal(1, runtime.CreateCount);
    }

    [Fact]
    public async Task EnsureCreatedAsync_RemovesAContainerWhoseCreationFailed()
    {
        // The daemon may have created the container before the request failed, and its id is unknown: the name the
        // library gave it is what removes it.
        var runtime = new InMemoryRuntime { FailCreate = true };
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test")) { Runtime = runtime };

        await using var container = definition.CreateContainer();
        await Assert.ThrowsAsync<InvalidOperationException>(() => container.EnsureCreatedAsync(XunitCancellationToken));

        Assert.Equal([container.Definition.Name], runtime.Deleted);
    }

    [Fact]
    public async Task EnsureCreatedAsync_RefusesAReusedContainerCreatedFromAnotherDefinition()
    {
        var runtime = new InMemoryRuntime();
        var original = new ContainerDefinition(ImageSource.FromExisting("sha256:test")) { Runtime = runtime, ReuseId = "reuse" };
        original.Environment.Add("MODE", "first");
        await using (var probe = original.CreateContainer())
        {
            runtime.Labels[ResourceLabels.ConfigurationHash] = probe.Definition.ConfigurationHash!;
        }

        var changed = new ContainerDefinition(original);
        changed.Environment.Add("MODE", "second");

        await using var sameContainer = original.CreateContainer();
        await sameContainer.EnsureCreatedAsync(XunitCancellationToken);

        await using var changedContainer = changed.CreateContainer();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => changedContainer.EnsureCreatedAsync(XunitCancellationToken));
        Assert.Contains("different definition", exception.Message);
        Assert.Throws<InvalidOperationException>(() => changedContainer.Id);
    }

    [Fact]
    public async Task EnsureCreatedAsync_ReadsBackTheCredentialsAReusedContainerWasCreatedWith()
    {
        // Every process generates its own password, and the adopted container only knows the one of the process that
        // created it.
        var runtime = new InMemoryRuntime();
        var creator = ContainerDefinition.CreateSqlServer();
        creator.Runtime = runtime;
        creator.ReuseId = "shared-sql";
        await using (var probe = creator.CreateContainer())
        {
            runtime.Labels[ResourceLabels.ConfigurationHash] = probe.Definition.ConfigurationHash!;
        }
        runtime.Environment["MSSQL_SA_PASSWORD"] = creator.SaPassword;
        runtime.Environment["SA_PASSWORD"] = creator.SaPassword;

        var adopter = ContainerDefinition.CreateSqlServer();
        adopter.Runtime = runtime;
        adopter.ReuseId = "shared-sql";
        Assert.NotEqual(creator.SaPassword, adopter.SaPassword);

        await using var container = adopter.CreateContainer();
        await container.EnsureCreatedAsync(XunitCancellationToken);

        Assert.Equal(creator.SaPassword, container.Definition.Environment.GetValue("MSSQL_SA_PASSWORD"));
    }

    [Fact]
    public async Task WaitUntilReadyAsync_ReportsTheTimeoutWithWhatExplainsIt()
    {
        var runtime = new InMemoryRuntime();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test"))
        {
            Runtime = runtime,
            StartupTimeout = TimeSpan.FromMilliseconds(200),
        };
        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));

        await using var container = definition.CreateContainer();
        runtime.OnStart = () => runtime.Emit(LogStream.Stderr, "waiting for the configuration");

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => container.StartAsync(XunitCancellationToken));
        Assert.IsAssignableTo<OperationCanceledException>(exception.InnerException);
        Assert.Contains("log message 'SERVER READY'", exception.Message);
        Assert.Contains("Running", exception.Message);
        Assert.Contains("[stderr] waiting for the configuration", exception.Message);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_DoesNotReportTheCancellationOfAStrategyAsTheStartupTimeout()
    {
        var runtime = new InMemoryRuntime();
        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test")) { Runtime = runtime };
        definition.WaitStrategies.Add(new ThrowingWaitStrategy(new TaskCanceledException("The HTTP client timed out.")));

        await using var container = definition.CreateContainer();

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => container.StartAsync(XunitCancellationToken));
        Assert.Equal("The HTTP client timed out.", exception.Message);
    }

    [Fact]
    public void WaitStrategies_DescribeThemselves()
    {
        Assert.Equal("port 8080", Wait.ForPort(8080).ToString());
        Assert.Equal("log message 'READY' (2 occurrence(s))", Wait.ForLogMessage("READY", occurrences: 2).ToString());
        Assert.Equal("command 'pg_isready --host 127.0.0.1'", new CommandWaitStrategy(["pg_isready", "--host", "127.0.0.1"]).ToString());
    }

    private sealed class ThrowingWaitStrategy(Exception exception) : IWaitStrategy
    {
        public Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken) => Task.FromException(exception);
    }

    private sealed class InMemoryRuntime : ContainerRuntime
    {
        private readonly List<LogEntry> _logs = [];
        private int _logStreamGeneration;
        private int _logAttachCount;
        private int _inspectCount;
        private long _clock;

        public InMemoryRuntime()
            : base("InMemory")
        {
        }

        public ContainerState State { get; set; } = ContainerState.Created;

        public bool LogsHaveTimestamps { get; init; }

        public Action? OnStart { get; set; }

        public bool FailNextInspect { get; set; }

        public bool FailCreate { get; set; }

        public int CreateCount { get; private set; }

        public List<string> Deleted { get; } = [];

        public Dictionary<string, string> Labels { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

        private DateTimeOffset? StartedAt { get; set; }

        /// <summary>Gets the number of times the log stream has been attached to.</summary>
        public int LogAttachCount => Volatile.Read(ref _logAttachCount);

        public override Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        internal override bool LogsIncludeTimestamps => LogsHaveTimestamps;

        internal override Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
        {
            if (FailCreate)
                throw new InvalidOperationException("The daemon did not answer.");

            if (State is ContainerState.Removed)
                State = ContainerState.Created;

            CreateCount++;
            return Task.FromResult("id");
        }

        internal override Task StartAsync(string id, CancellationToken cancellationToken)
        {
            State = ContainerState.Running;

            // The timestamps of the entries and the start time come from the same clock, and a start happens after every
            // entry written before it.
            StartedAt = NextTimestamp();
            OnStart?.Invoke();
            return Task.CompletedTask;
        }

        internal override Task StopAsync(string id, CancellationToken cancellationToken)
        {
            State = ContainerState.Exited;
            return Task.CompletedTask;
        }

        internal override async Task RestartAsync(string id, CancellationToken cancellationToken)
        {
            await StopAsync(id, cancellationToken);
            await StartAsync(id, cancellationToken);
        }

        internal override Task DeleteAsync(string id, CancellationToken cancellationToken)
        {
            Deleted.Add(id);
            State = ContainerState.Removed;
            return Task.CompletedTask;
        }

        internal override Task<bool> ExistsAsync(string id, CancellationToken cancellationToken) => Task.FromResult(State is not ContainerState.Removed);

        internal override Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _inspectCount);
            if (FailNextInspect)
            {
                FailNextInspect = false;
                throw new InvalidOperationException("The daemon did not answer.");
            }

            var info = new ContainerInfo
            {
                Id = id,
                Name = "name",
                State = State,
                StartedAt = StartedAt,
                Labels = Labels,
                Environment = Environment,
            };

            return Task.FromResult(info);
        }

        /// <summary>Waits until the log stream is attached to more than <paramref name="count"/> times.</summary>
        public async Task WaitForLogAttachAsync(int count)
        {
            while (LogAttachCount <= count)
                await Task.Delay(10, XunitCancellationToken);
        }

        /// <summary>Waits until the container is inspected again, which is what the log pump and the wait do once a log stream ends.</summary>
        public async Task WaitForInspectionAsync()
        {
            var initial = Volatile.Read(ref _inspectCount);
            while (Volatile.Read(ref _inspectCount) <= initial)
                await Task.Delay(10, XunitCancellationToken);
        }

        internal override async IAsyncEnumerable<LogEntry> GetLogsAsync(string id, bool follow, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Mimics the real runtimes: every attach replays the log from the beginning, and the stream can end on
            // its own while the container is still running.
            if (follow)
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
                    if (!follow)
                        yield break;

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
                _logs.Add(new LogEntry(stream, message, LogsHaveTimestamps ? NextTimestamp() : null));
            }
        }

        /// <summary>A clock that never gives the same time twice, unlike the system clock, whose resolution can be coarse.</summary>
        private DateTimeOffset NextTimestamp() => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Increment(ref _clock));

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

        /// <summary>Waits for a message, bounded by the test timeout rather than a budget of its own: a thread pool that is busy with the other tests can delay the pump for a long time.</summary>
        public async Task WaitForMessageAsync(string message)
        {
            while (!_messages.Any(value => string.Equals(value, message, StringComparison.Ordinal)))
                await Task.Delay(20, XunitCancellationToken);
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
