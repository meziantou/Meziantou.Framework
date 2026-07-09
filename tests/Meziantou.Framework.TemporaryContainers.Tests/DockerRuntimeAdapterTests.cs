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

    [Fact]
    public void WslcCreateArguments_DoNotUsePullOption()
    {
        var adapter = ContainerRuntimeAdapter.Create(ContainerRuntime.Wslc, "wslc");
        var definition = new ContainerDefinition(new RegistryImage("busybox:1.37"));

        var args = adapter.BuildCreateArguments(definition, "busybox:1.37");

        Assert.DoesNotContain("--pull", args);
    }

    [Fact]
    public void ParseInspect_UsesTopLevelPorts()
    {
        var adapter = ContainerRuntimeAdapter.Create(ContainerRuntime.Wslc, "wslc");

        var inspectOutput =
                """
                [
                    {
                        "Id": "container-id",
                        "Name": "test",
                        "Image": "busybox:1.37",
                        "Ports": {
                            "8080/tcp": [
                                {
                                    "HostIp": "127.0.0.1",
                                    "HostPort": "50809"
                                }
                            ]
                        },
                        "State": {
                            "Status": "running",
                            "StartedAt": "2026-01-01T00:00:00Z",
                            "FinishedAt": "0001-01-01T00:00:00Z",
                            "ExitCode": 0
                        },
                        "Labels": {
                            "k": "v"
                        }
                    }
                ]
                """;

        var container = adapter.ParseInspect(inspectOutput);

        Assert.Equal(50809, container.Ports[8080]);
        Assert.Equal("v", container.Labels["k"]);
    }
}
