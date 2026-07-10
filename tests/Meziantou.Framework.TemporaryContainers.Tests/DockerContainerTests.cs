namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.Docker)
{
    [Fact]
    public Task PauseAndUnpause() => AssertPauseUnpauseAsync();
}
