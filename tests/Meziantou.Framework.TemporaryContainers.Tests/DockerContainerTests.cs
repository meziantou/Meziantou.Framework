using Meziantou.Xunit;

namespace Meziantou.Framework.TemporaryContainers.Tests;

// Each test starts its own container. Running them all at once saturates the CI agents and makes the container
// runtimes fail transiently (image pull races, port collisions), so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class DockerContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.Docker)
{
    // The Linux CI agents ship the Docker daemon, so a missing docker there means the CI setup regressed. Skipping would hide it.
    // The Windows agents are left out on purpose: their docker is flaky enough that requiring it would fail runs for reasons unrelated to the change under test.
    protected override bool IsRuntimeRequired => OperatingSystem.IsLinux() && TestEnvironment.IsOnGitHubActions();

    [Fact]
    public Task PauseAndUnpause() => AssertPauseUnpauseAsync();

    [Fact]
    public Task StartAsync_ContainerExitsBeforeTheReadyMessage_ReportsWhatTheContainerPrinted() => AssertStartFailureReportsContainerOutputAsync();

    [Fact]
    public Task FailedCommand_ReportsWhatTheRuntimeComplainedAbout() => AssertFailedCommandReportsWhatTheRuntimeComplainedAboutAsync();
}
