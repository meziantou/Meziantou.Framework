using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerRuntimeAdapterTests
{
    [Fact]
    public void DockerUsesDockerDialect()
    {
        var adapter = ContainerRuntimeAdapter.Create(ContainerRuntime.Docker, "docker");

        Assert.Equal(ContainerRuntime.Docker, adapter.Runtime);
        Assert.IsAssignableTo<DockerRuntimeAdapter>(adapter);
        Assert.True(adapter.SupportsPause);
        Assert.True(adapter.SupportsRestart);
        Assert.True(adapter.LogsIncludeTimestamps);
        Assert.Equal("rm -f abc", string.Join(' ', adapter.BuildRemoveArguments("abc")));
        Assert.Equal("cp src abc:/dst", string.Join(' ', adapter.BuildCopyToContainerArguments("abc", "src", "/dst")));
        Assert.Equal("cp abc:/src dst", string.Join(' ', adapter.BuildCopyFromContainerArguments("abc", "/src", "dst")));
        Assert.Contains("--timestamps", adapter.BuildLogsArguments("abc"));
    }

    [Fact]
    public void PodmanUsesDockerDialect()
    {
        var adapter = ContainerRuntimeAdapter.Create(ContainerRuntime.Podman, "podman");

        Assert.Equal(ContainerRuntime.Podman, adapter.Runtime);
        Assert.IsAssignableTo<DockerRuntimeAdapter>(adapter);
    }

    [Fact]
    public void WslcUsesDockerDialect()
    {
        var adapter = ContainerRuntimeAdapter.Create(ContainerRuntime.Wslc, "wslc");

        Assert.Equal(ContainerRuntime.Wslc, adapter.Runtime);
        Assert.IsAssignableTo<DockerRuntimeAdapter>(adapter);
    }
}
