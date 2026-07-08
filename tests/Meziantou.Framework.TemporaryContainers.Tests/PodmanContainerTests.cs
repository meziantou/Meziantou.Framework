namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class PodmanContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.Podman)
{
    [Fact]
    public Task PauseAndUnpause() => AssertPauseUnpauseAsync();
}
