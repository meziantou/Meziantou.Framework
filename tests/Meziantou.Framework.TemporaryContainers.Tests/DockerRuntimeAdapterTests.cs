using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerRuntimeAdapterTests
{
    [Fact]
    public void FormatCommand_QuotesArgumentsContainingSpaces()
    {
        var command = ContainerCli.FormatCommand("docker", ["create", "--name", "my container", "busybox:1.37"]);

        Assert.Equal("""docker create --name "my container" busybox:1.37""", command);
    }

    [Fact]
    public void FormatCommand_EscapesQuotesInsideArguments()
    {
        var command = ContainerCli.FormatCommand("docker", ["exec", "-c", "echo \"hi\"", "busybox"]);

        Assert.Equal("""docker exec -c "echo \"hi\"" busybox""", command);
    }

    [Fact]
    public void FormatCommand_RedactsEnvironmentVariableValues()
    {
        var command = ContainerCli.FormatCommand("docker", ["create", "--env", "POSTGRES_PASSWORD=hunter2", "-e", "TOKEN=abc", "postgres:16"]);

        Assert.Equal("docker create --env POSTGRES_PASSWORD=*** -e TOKEN=*** postgres:16", command);
        Assert.DoesNotContain("hunter2", command);
        Assert.DoesNotContain("abc", command);
    }

    [Fact]
    public void FormatCommand_RedactsEnvironmentVariableWithoutValue()
    {
        var command = ContainerCli.FormatCommand("docker", ["create", "--env", "PATH_FROM_HOST", "busybox:1.37"]);

        Assert.Equal("docker create --env *** busybox:1.37", command);
    }

    [Fact]
    public void FormatCommand_KeepsArgumentsThatOnlyLookLikeEnvironmentVariables()
    {
        var command = ContainerCli.FormatCommand("docker", ["create", "--label", "owner=meziantou", "busybox:1.37"]);

        Assert.Equal("docker create --label owner=meziantou busybox:1.37", command);
    }

    [Fact]
    public void DockerUsesDockerDialect()
    {
        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Docker);

        Assert.True(runtime.SupportsPause);
        Assert.True(runtime.SupportsRestart);
        Assert.True(runtime.LogsIncludeTimestamps);
        Assert.Equal("rm -f -v abc", string.Join(' ', runtime.BuildRemoveArguments("abc")));
        Assert.Equal("cp src abc:/dst", string.Join(' ', runtime.BuildCopyToContainerArguments("abc", "src", "/dst")));
        Assert.Equal("cp abc:/src dst", string.Join(' ', runtime.BuildCopyFromContainerArguments("abc", "/src", "dst")));
        Assert.Contains("--timestamps", runtime.BuildLogsArguments("abc"));
        Assert.Equal("version", string.Join(' ', runtime.BuildProbeArguments()));
    }

    [Fact]
    public void PodmanUsesDockerDialect()
    {
        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Podman);

        Assert.Equal("version", string.Join(' ', runtime.BuildProbeArguments()));
        Assert.Equal("rm -f -v abc", string.Join(' ', runtime.BuildRemoveArguments("abc")));
    }

    [Fact]
    public void WslcUsesDockerDialect()
    {
        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Wslc);

        Assert.Equal("list -q", string.Join(' ', runtime.BuildProbeArguments()));

        // wslc has no '-v' flag on 'rm', and no volume commands at all.
        Assert.Equal("rm -f abc", string.Join(' ', runtime.BuildRemoveArguments("abc")));
        Assert.Throws<NotSupportedException>(() => runtime.BuildCreateVolumeArguments(new VolumeDefinition(), "vol"));
        Assert.Throws<NotSupportedException>(() => runtime.BuildDeleteVolumeArguments("vol"));
        Assert.Throws<NotSupportedException>(() => runtime.BuildVolumeExistsArguments("vol"));
    }

    [Fact]
    public void BuildsVolumeArguments()
    {
        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Docker);
        var definition = new VolumeDefinition { Driver = "local" };
        definition.Labels.Add("owner", "meziantou");
        definition.DriverOptions.Add("size", "10m");

        Assert.Equal("volume create --driver local --label owner=meziantou --opt size=10m my-volume", string.Join(' ', runtime.BuildCreateVolumeArguments(definition, "my-volume")));

        // '--force' is not used: podman would remove the containers still using the volume.
        Assert.Equal("volume rm my-volume", string.Join(' ', runtime.BuildDeleteVolumeArguments("my-volume")));
        Assert.Equal("volume inspect my-volume", string.Join(' ', runtime.BuildVolumeExistsArguments("my-volume")));
    }

    [Fact]
    public async Task IsSupportedAsync_ReturnsTrueWhenTheProbeCommandSucceeds()
    {
        var executable = CreateStubCli(exitCode: 0);
        try
        {
            var runtime = new DockerContainerRuntime(nameof(ContainerRuntime.Docker), DockerContainerRuntime.Flavor.Docker, executable);

            Assert.True(await runtime.IsSupportedAsync(XunitCancellationToken));
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task IsSupportedAsync_ReturnsFalseWhenTheProbeCommandFails()
    {
        // The daemon is not reachable: the CLI is there, but every command it runs fails.
        var executable = CreateStubCli(exitCode: 1);
        try
        {
            var runtime = new DockerContainerRuntime(nameof(ContainerRuntime.Docker), DockerContainerRuntime.Flavor.Docker, executable);

            Assert.False(await runtime.IsSupportedAsync(XunitCancellationToken));
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task IsSupportedAsync_ReturnsFalseWhenTheExecutableCannotBeStarted()
    {
        var missing = Path.Combine(Path.GetTempPath(), "MezTC-missing-" + Guid.NewGuid().ToString("N"));
        var runtime = new DockerContainerRuntime(nameof(ContainerRuntime.Docker), DockerContainerRuntime.Flavor.Docker, missing);

        Assert.False(await runtime.IsSupportedAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task IsSupportedAsync_CachesTheSuccessfulProbe()
    {
        var executable = CreateStubCli(exitCode: 0);
        var runtime = new DockerContainerRuntime(nameof(ContainerRuntime.Docker), DockerContainerRuntime.Flavor.Docker, executable);
        try
        {
            Assert.True(await runtime.IsSupportedAsync(XunitCancellationToken));
        }
        finally
        {
            File.Delete(executable);
        }

        // The CLI is gone, so a second probe would fail: the runtime must answer from what it already knows.
        Assert.True(await runtime.IsSupportedAsync(XunitCancellationToken));
    }

    [Fact]
    public void WslcCreateArguments_DoNotUsePullOption()
    {
        var definition = new ContainerDefinition(new RegistryImage("busybox:1.37"));

        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Wslc);
        var args = runtime.BuildCreateArguments(definition, "busybox:1.37");

        Assert.DoesNotContain("--pull", args);
    }

    [Fact]
    public void ParseInspect_UsesTopLevelPorts()
    {
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

        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Wslc);
        var container = runtime.ParseInspect(inspectOutput);

        Assert.Equal(50809, container.Ports[8080]);
        Assert.Equal("v", container.Labels["k"]);
    }

    [Fact]
    public void ParseInspect_ConvertsLargeUnsignedExitCodeToSignedInt()
    {
        var inspectOutput =
                """
                [
                    {
                        "Id": "container-id",
                        "Name": "test",
                        "Image": "windows/servercore:ltsc2022",
                        "State": {
                            "Status": "exited",
                            "StartedAt": "2026-01-01T00:00:00Z",
                            "FinishedAt": "2026-01-01T00:00:10Z",
                            "ExitCode": 3221225786
                        }
                    }
                ]
                """;

        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Docker);
        var container = runtime.ParseInspect(inspectOutput);

        Assert.Equal(unchecked((int)3221225786u), container.ExitCode);
    }

    [Theory]
    [InlineData("Loaded image: repo/app:1.2", "repo/app:1.2")]
    [InlineData("Loaded image ID: sha256:abc123", "sha256:abc123")]
    [InlineData("some noise\nLoaded image: repo/app:latest\nmore noise", "repo/app:latest")]
    [InlineData("  Loaded image: repo/app:latest  ", "repo/app:latest")]
    public void TryParseLoadedImage_ReadsTheImageReference(string output, string expected)
    {
        Assert.Equal(expected, ContainerImageOutputParser.TryParseLoadedImage(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("something went wrong")]
    public void TryParseLoadedImage_ReturnsNullWithoutAMarker(string output)
    {
        Assert.Null(ContainerImageOutputParser.TryParseLoadedImage(output));
    }

    /// <summary>Writes a CLI that exits with <paramref name="exitCode"/> whatever it is asked to do, so the outcome of the probe can be forced.</summary>
    private static string CreateStubCli(int exitCode)
    {
        var path = Path.Combine(Path.GetTempPath(), "MezTC-stub-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            path += ".cmd";
            File.WriteAllText(path, $"@exit /b {exitCode}\r\n");
        }
        else
        {
            path += ".sh";
            File.WriteAllText(path, $"#!/bin/sh\nexit {exitCode}\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
