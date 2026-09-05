namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class AppleContainerContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.AppleContainer)
{
    // Unlike WslcContainerTests, this class does not make the runtime required on CI. The macOS agents install it
    // (.github/actions/setup-apple-container), but Apple boots a virtual machine per container and the GitHub-hosted
    // macOS agents are themselves virtual machines without nested virtualization, so the service cannot start there
    // (actions/runner-images#13565). The debug_apple_container job reports the day that changes; requiring the
    // runtime here before then would only turn the macOS legs red.

    [Fact]
    public async Task PauseAsync_IsNotSupported()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());
        await Assert.ThrowsAsync<NotSupportedException>(() => container.PauseAsync(XunitCancellationToken));
    }

    [Fact]
    public Task FailedCommand_ReportsWhatTheRuntimeComplainedAbout() => AssertFailedCommandReportsWhatTheRuntimeComplainedAboutAsync();
}
