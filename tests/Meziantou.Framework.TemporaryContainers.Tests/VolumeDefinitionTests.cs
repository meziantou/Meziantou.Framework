using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class VolumeDefinitionTests
{
    [Fact]
    public void CreateVolume_GeneratesANameEveryRuntimeAccepts()
    {
        var first = new VolumeDefinition().CreateVolume().Name;
        var second = new VolumeDefinition().CreateVolume().Name;

        Assert.NotEqual(first, second);
        Assert.StartsWith("meziantou-tc-", first);
        Assert.All(first, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-', $"'{c}' is not accepted in a volume name."));
    }

    [Fact]
    public void CreateVolume_DerivesADeterministicNameFromTheReuseId()
    {
        var definition = new VolumeDefinition { ReuseId = "my/reuse:id" };

        var name = definition.CreateVolume().Name;

        Assert.Equal(name, definition.CreateVolume().Name);
        Assert.Equal("meziantou-tc-my-reuse-id", name);
    }

    [Fact]
    public void CreateVolume_KeepsAnExplicitName()
    {
        var volume = new VolumeDefinition { Name = "my-volume" }.CreateVolume();

        Assert.Equal("my-volume", volume.Name);
    }

    [Fact]
    public void CreateVolume_DeepClonesDefinition()
    {
        var definition = new VolumeDefinition();
        definition.Labels.Add("a", "1");
        definition.DriverOptions.Add("size", "10m");

        var volume = definition.CreateVolume();

        definition.Labels.Add("b", "2");
        definition.DriverOptions.Add("type", "tmpfs");

        Assert.True(volume.Definition.Labels.Contains("a"));
        Assert.False(volume.Definition.Labels.Contains("b"));
        Assert.True(volume.Definition.DriverOptions.Contains("size"));
        Assert.False(volume.Definition.DriverOptions.Contains("type"));
    }

    [Fact]
    public void CopyConstructor_CopiesScalarProperties()
    {
        var original = new VolumeDefinition
        {
            Runtime = ContainerRuntime.Podman,
            Name = "name",
            Driver = "local",
            ReuseId = "reuse",
        };

        var copy = new VolumeDefinition(original);

        Assert.Equal(ContainerRuntime.Podman, copy.Runtime);
        Assert.Equal("name", copy.Name);
        Assert.Equal("local", copy.Driver);
        Assert.Equal("reuse", copy.ReuseId);
    }

    [Fact]
    public async Task EnsureCreatedAsync_CreatesTheVolumeOnce()
    {
        var runtime = new RecordingRuntime();
        var volume = new VolumeDefinition { Runtime = runtime }.CreateVolume();

        await volume.EnsureCreatedAsync(XunitCancellationToken);
        await volume.EnsureCreatedAsync(XunitCancellationToken);

        Assert.Equal([volume.Name], runtime.Created);
    }

    [Fact]
    public async Task DisposeAsync_RemovesTheVolumeItCreated()
    {
        var runtime = new RecordingRuntime();
        var volume = new VolumeDefinition { Runtime = runtime }.CreateVolume();

        await volume.EnsureCreatedAsync(XunitCancellationToken);
        await volume.DisposeAsync();

        Assert.Equal([volume.Name], runtime.Deleted);
    }

    [Fact]
    public async Task DisposeAsync_KeepsAVolumeThatAlreadyExisted()
    {
        var runtime = new RecordingRuntime();
        runtime.Volumes.Add("my-volume");
        var volume = new VolumeDefinition { Runtime = runtime, Name = "my-volume" }.CreateVolume();

        await volume.EnsureCreatedAsync(XunitCancellationToken);
        await volume.DisposeAsync();

        Assert.Empty(runtime.Created);
        Assert.Empty(runtime.Deleted);
    }

    [Fact]
    public async Task DisposeAsync_KeepsAReusedVolume()
    {
        var runtime = new RecordingRuntime();
        var volume = new VolumeDefinition { Runtime = runtime, ReuseId = "reuse" }.CreateVolume();

        await volume.EnsureCreatedAsync(XunitCancellationToken);
        await volume.DisposeAsync();

        Assert.Equal([volume.Name], runtime.Created);
        Assert.Empty(runtime.Deleted);
    }

    [Fact]
    public async Task DeleteAsync_ReportsAVolumeThatSurvived()
    {
        var runtime = new RecordingRuntime { IgnoreDeletions = true };
        await using var volume = new VolumeDefinition { Runtime = runtime }.CreateVolume();
        await volume.EnsureCreatedAsync(XunitCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => volume.DeleteAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task StartAsync_CreatesTheMountedVolume()
    {
        var runtime = new RecordingRuntime();
        await using var volume = new VolumeDefinition { Runtime = runtime }.CreateVolume();

        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test")) { Runtime = runtime };
        definition.Mounts.AddVolume(volume, "/data");

        await using var container = definition.CreateContainer();
        await container.StartAsync(XunitCancellationToken);

        Assert.Equal([volume.Name], runtime.Created);
    }

    [Fact]
    public async Task StartAsync_RejectsAVolumeFromAnotherRuntime()
    {
        var containerRuntime = new RecordingRuntime();
        await using var volume = new VolumeDefinition { Runtime = new RecordingRuntime() }.CreateVolume();

        var definition = new ContainerDefinition(ImageSource.FromExisting("sha256:test")) { Runtime = containerRuntime };
        definition.Mounts.AddVolume(volume, "/data");

        await using var container = definition.CreateContainer();

        await Assert.ThrowsAsync<InvalidOperationException>(() => container.StartAsync(XunitCancellationToken));
    }

    /// <summary>A runtime that keeps its volumes in memory, so the ownership rules can be exercised without a daemon.</summary>
    private sealed class RecordingRuntime : ContainerRuntime
    {
        public RecordingRuntime()
            : base("Recording")
        {
        }

        public HashSet<string> Volumes { get; } = new(StringComparer.Ordinal);

        public List<string> Created { get; } = [];

        public List<string> Deleted { get; } = [];

        /// <summary>Mimics a runtime that reports success but leaves the volume behind, as it does when a container still uses it.</summary>
        public bool IgnoreDeletions { get; set; }

        public override Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        internal override Task CreateVolumeAsync(VolumeDefinition definition, string name, CancellationToken cancellationToken)
        {
            Created.Add(name);
            Volumes.Add(name);
            return Task.CompletedTask;
        }

        internal override Task DeleteVolumeAsync(string name, CancellationToken cancellationToken)
        {
            Deleted.Add(name);
            if (!IgnoreDeletions)
                Volumes.Remove(name);

            return Task.CompletedTask;
        }

        internal override Task<bool> VolumeExistsAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Volumes.Contains(name));

        internal override Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken) => Task.FromResult("id");

        internal override Task StartAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

        internal override Task DeleteAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

        internal override Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerInfo { Id = id, Name = "name", State = ContainerState.Running });

        internal override IAsyncEnumerable<LogEntry> GetLogsAsync(string id, CancellationToken cancellationToken) => AsyncEnumerable.Empty<LogEntry>();

        internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition) => new Dictionary<int, int>();
    }
}
