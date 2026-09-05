namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class AppleContainerContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.AppleContainer)
{
    [Fact]
    public async Task PauseAsync_IsNotSupported()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());
        await Assert.ThrowsAsync<NotSupportedException>(() => container.PauseAsync(XunitCancellationToken));
    }

    [Fact]
    public Task FailedCommand_ReportsWhatTheRuntimeComplainedAbout() => AssertFailedCommandReportsWhatTheRuntimeComplainedAboutAsync();
}
