using Meziantou.Xunit;

namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class PodmanContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.Podman)
{
    // The Linux CI agents ship podman, so a missing podman there means the CI setup regressed. Skipping would hide it.
    protected override bool IsRuntimeRequired => OperatingSystem.IsLinux() && TestEnvironment.IsOnGitHubActions();

    [Fact]
    public Task PauseAndUnpause() => AssertPauseUnpauseAsync();

    [Fact]
    public Task FailedCommand_ReportsWhatTheRuntimeComplainedAbout() => AssertFailedCommandReportsWhatTheRuntimeComplainedAboutAsync();

    [Fact]
    public Task Volume_IsCreatedWithTheContainerAndRemovedOnDemand() => AssertVolumeLifecycleAsync();

    [Fact]
    public Task Volume_CarriesItsContentToTheNextContainer() => AssertVolumeSharedBetweenContainersAsync();

    [Fact]
    public Task Volume_ReadOnlyMountRejectsWrites() => AssertReadOnlyVolumeMountAsync();
}
