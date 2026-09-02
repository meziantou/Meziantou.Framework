namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class DockerContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.Docker)
{
    [Fact]
    public Task PauseAndUnpause() => AssertPauseUnpauseAsync();

    [Fact]
    public Task StartAsync_ContainerExitsBeforeTheReadyMessage_ReportsWhatTheContainerPrinted() => AssertStartFailureReportsContainerOutputAsync();
}
