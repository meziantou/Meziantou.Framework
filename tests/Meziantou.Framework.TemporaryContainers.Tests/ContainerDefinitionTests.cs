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
}
