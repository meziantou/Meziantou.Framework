namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class AppleContainerContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.AppleContainer)
{
    // Unlike WslcContainerTests, this class does not make the runtime required on CI. The macOS agents install it
    // (.github/actions/setup-apple-container) and its service does start there, so the 'container ls -q' probe
    // behind IsSupportedAsync answers; only booting the virtual machine a container needs fails, with
    // 'Virtualization is not available on this hardware' (actions/runner-images#13565). The setup action therefore
    // stops the service when no container can boot, which is what keeps these tests skipping rather than failing.
    // The debug_apple_container job reports the day that changes.

    [Fact]
    public async Task PauseAsync_IsNotSupported()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());
        await Assert.ThrowsAsync<NotSupportedException>(() => container.PauseAsync(XunitCancellationToken));
    }

    [Fact]
    public Task FailedCommand_ReportsWhatTheRuntimeComplainedAbout() => AssertFailedCommandReportsWhatTheRuntimeComplainedAboutAsync();
}
