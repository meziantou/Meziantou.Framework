using System.Net;
using System.Net.Sockets;
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
    public void FormatCommand_RedactsVolumeDriverOptionValues()
    {
        var command = ContainerCli.FormatCommand("docker", ["volume", "create", "--opt", "password=hunter2", "-o", "o=addr=10.0.0.1", "data"]);

        Assert.Equal("docker volume create --opt password=*** -o o=*** data", command);
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
        Assert.Equal("version --format {{.Server.Os}}", string.Join(' ', runtime.BuildProbeArguments()));
        Assert.Equal("logs --timestamps abc", string.Join(' ', runtime.BuildLogsArguments("abc", follow: false)));
        Assert.Equal("image rm meziantou-tc/abc:latest", string.Join(' ', runtime.BuildDeleteImageArguments("meziantou-tc/abc:latest")));
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
    public void BuildsListArgumentsForTheCleanup()
    {
        var docker = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Docker);
        var wslc = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Wslc);

        Assert.Equal($"ps -a --no-trunc --filter label={ResourceLabels.Managed} --format {{{{.ID}}}}", string.Join(' ', docker.BuildListManagedContainersArguments()));
        Assert.Equal($"volume ls --filter label={ResourceLabels.Managed} --format {{{{.Name}}}}", string.Join(' ', docker.BuildListManagedVolumesArguments()));

        // wslc has no '--format' for its listing and no volume commands at all.
        Assert.Equal($"list -a -q --filter label={ResourceLabels.Managed}", string.Join(' ', wslc.BuildListManagedContainersArguments()));
        Assert.Throws<NotSupportedException>(() => wslc.BuildListManagedVolumesArguments());
    }

    [Fact]
    public void BuildsVolumeArguments()
    {
        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Docker);
        var definition = new VolumeDefinition { Driver = "local" };
        definition.Labels.Add("owner", "meziantou");
        definition.DriverOptions.Add("size", "10m");

        // The library also stamps the labels that identify the run, whose values change from one run to the next.
        var createArguments = string.Join(' ', runtime.BuildCreateVolumeArguments(definition, "my-volume"));
        Assert.StartsWith("volume create --driver local --label owner=meziantou --label ", createArguments);
        Assert.Contains($"--label {ResourceLabels.Managed}=1", createArguments);
        Assert.EndsWith("--opt size=10m my-volume", createArguments);

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
    public async Task ExistsAsync_ReportsAMissingContainerWhenTheDaemonAnswers()
    {
        var executable = CreateStubCli(exitCode: 0, failingCommand: "container");
        try
        {
            var runtime = new DockerContainerRuntime(nameof(ContainerRuntime.Docker), DockerContainerRuntime.Flavor.Docker, executable);

            Assert.False(await runtime.ExistsAsync("abc", XunitCancellationToken));
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task ExistsAsync_ThrowsWhenTheDaemonDoesNotAnswer()
    {
        // Every command fails, the probe included: that is a daemon that does not answer, not a missing container, and a
        // cleanup must not report the container as removed.
        var executable = CreateStubCli(exitCode: 1);
        try
        {
            var runtime = new DockerContainerRuntime(nameof(ContainerRuntime.Docker), DockerContainerRuntime.Flavor.Docker, executable);

            await Assert.ThrowsAsync<ContainerRuntimeException>(() => runtime.ExistsAsync("abc", XunitCancellationToken));
            await Assert.ThrowsAsync<ContainerRuntimeException>(() => runtime.DeleteAsync("abc", XunitCancellationToken));
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task DeleteAsync_ReportsAContainerThatIsStillThere()
    {
        // 'rm' fails while 'inspect' still finds the container: the runtime could not remove it.
        var executable = CreateStubCli(exitCode: 0, failingCommand: "rm");
        try
        {
            var runtime = new DockerContainerRuntime(nameof(ContainerRuntime.Docker), DockerContainerRuntime.Flavor.Docker, executable);

            await Assert.ThrowsAsync<ContainerRuntimeException>(() => runtime.DeleteAsync("abc", XunitCancellationToken));
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public void DockerCreateArguments_PullNothingSinceTheImageIsAlreadyPrepared()
    {
        var definition = new ContainerDefinition(new RegistryImage("busybox:1.37"));
        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Docker);

        var args = runtime.BuildCreateArguments(definition, "busybox:1.37");

        Assert.Equal("never", args[args.ToList().IndexOf("--pull") + 1]);
    }

    [Fact]
    public void CreateArguments_PassTheEnvironmentThroughAFile()
    {
        var definition = new ContainerDefinition(new RegistryImage("busybox:1.37"));
        definition.Environment.Add("PASSWORD", "hunter2");
        definition.Environment.Add("MULTILINE", "first\nsecond");
        var runtime = Assert.IsAssignableTo<DockerContainerRuntime>(ContainerRuntime.Docker);

        using var environmentFile = EnvironmentFile.Create(definition.Environment);
        var args = runtime.BuildCreateArguments(definition, "busybox:1.37", environmentFile);

        // The values of a command line can be read by every user of the machine; the file only by the current one.
        Assert.NotNull(environmentFile);
        Assert.Equal(environmentFile.Path, args[args.ToList().IndexOf("--env-file") + 1]);
        Assert.DoesNotContain(args, arg => arg.Contains("hunter2", StringComparison.Ordinal));
        Assert.Equal("PASSWORD=hunter2\n", File.ReadAllText(environmentFile.Path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(environmentFile.Path));

        // A value that spans lines cannot be written to the file, so it stays on the command line.
        Assert.Contains("MULTILINE=first\nsecond", args);
    }

    [Theory]
    [InlineData("value", true)]
    [InlineData("va lue=with spaces", true)]
    [InlineData("multi\nline", false)]
    [InlineData("carriage\rreturn", false)]
    [InlineData("\"quoted\"", false)]
    [InlineData("'quoted'", false)]
    [InlineData("with # comment", false)]
    public void EnvironmentFile_OnlyHoldsTheValuesEveryRuntimeReadsTheSameWay(string value, bool expected)
    {
        Assert.Equal(expected, EnvironmentFile.CanBeWritten("NAME", value));
    }

    [Theory]
    [InlineData(null, 8080, true, "127.0.0.1::8080")]
    [InlineData(9090, 8080, true, "127.0.0.1:9090:8080")]
    [InlineData(null, 8080, false, "8080")]
    [InlineData(9090, 8080, false, "9090:8080")]
    public void FormatPort_PublishesOnTheLoopbackAddress(int? hostPort, int port, bool hostIpSupported, string expected)
    {
        Assert.Equal(expected, DockerCreateArgumentBuilder.FormatPort(new ContainerPort(hostPort, port), hostIpSupported));
    }

    [Fact]
    public void FormatPort_EnclosesAnIPv6AddressInBrackets()
    {
        Assert.Equal("[::1]::8080", DockerCreateArgumentBuilder.FormatPort(new ContainerPort(8080) with { HostIp = "::1" }, hostIpSupported: true));
    }

    [Theory]
    [InlineData("alpine", "alpine:latest")]
    [InlineData("library/alpine", "library/alpine:latest")]
    [InlineData("alpine:3.20", "alpine:3.20")]
    [InlineData("localhost:5000/app", "localhost:5000/app:latest")]
    [InlineData("localhost:5000/app:1", "localhost:5000/app:1")]
    [InlineData("alpine@sha256:abcd", "alpine@sha256:abcd")]
    public void WithDefaultTag_AddsLatestToAnUntaggedReference(string reference, string expected)
    {
        // The Engine API pulls every tag of an untagged reference.
        Assert.Equal(expected, ImageReference.WithDefaultTag(reference));
    }

    [Theory]
    [InlineData("tcp://10.0.0.1:2375", "http://10.0.0.1:2375/")]
    [InlineData("http://10.0.0.1:2375", "http://10.0.0.1:2375/")]
    public void TryParseDockerHost_ResolvesTheEndpoint(string dockerHost, string expectedDisplayName)
    {
        Assert.True(DockerApiTransport.TryParseDockerHost(dockerHost, tls: null, out var endpoint));
        Assert.Equal(expectedDisplayName, endpoint.DisplayName);
    }

    [Theory]
    [InlineData("unix:///var/run/docker.sock", "/var/run/docker.sock")]
    [InlineData("unix:///run/user/1000/docker%20sock", "/run/user/1000/docker sock")]
    public void TryParseDockerHost_ResolvesAUnixSocket(string dockerHost, string expectedPath)
    {
        global::Xunit.Assert.SkipWhen(OperatingSystem.IsWindows(), "A unix socket path is not a Windows path.");

        Assert.True(DockerApiTransport.TryParseDockerHost(dockerHost, tls: null, out var endpoint));
        Assert.Equal(FullPath.FromPath(expectedPath), endpoint.UnixSocketPath);
    }

    [Fact]
    public void TryParseDockerHost_UsesTlsWhenTheCliIsConfiguredForIt()
    {
        var tls = new DockerApiTransport.TlsSettings("/certs");

        Assert.True(DockerApiTransport.TryParseDockerHost("tcp://docker.example.com:2376", tls, out var endpoint));

        Assert.Equal("https://docker.example.com:2376/", endpoint.DisplayName);
        Assert.Same(tls, endpoint.Tls);
    }

    [Theory]
    [InlineData(@"npipe:////./pipe/docker_engine", ".", "docker_engine")]
    [InlineData(@"npipe://server/pipe/custom", "server", "custom")]
    public void TryParseDockerHost_ResolvesANamedPipe(string dockerHost, string expectedServer, string expectedName)
    {
        Assert.True(DockerApiTransport.TryParseDockerHost(dockerHost, tls: null, out var endpoint));
        Assert.Equal(expectedServer, endpoint.NamedPipeServer);
        Assert.Equal(expectedName, endpoint.NamedPipeName);
    }

    [Theory]
    [InlineData("ssh://user@host")]
    [InlineData("unix://")]
    [InlineData("not a uri")]
    public void TryParseDockerHost_RejectsAnUnsupportedValue(string dockerHost)
    {
        Assert.False(DockerApiTransport.TryParseDockerHost(dockerHost, tls: null, out _));
    }

    [Fact]
    public void ParseInspect_ReadsTheBindingsUnderNetworkSettings()
    {
        // docker and podman report one binding per address family for a port published on every interface, and podman
        // leaves HostIp empty.
        var inspectOutput =
                """
                [
                    {
                        "Id": "container-id",
                        "Name": "/test",
                        "Ports": {},
                        "Config": { "Env": ["PATH=/bin", "PASSWORD=a=b"] },
                        "NetworkSettings": {
                            "Ports": {
                                "8080/tcp": [ { "HostIp": "0.0.0.0", "HostPort": "50809" }, { "HostIp": "::", "HostPort": "50809" } ],
                                "5432/tcp": [ { "HostIp": "", "HostPort": "50810" } ],
                                "9090/tcp": null
                            }
                        }
                    }
                ]
                """;

        var container = DockerContainerInfoParser.ParseInspectOutput(inspectOutput);

        Assert.Equal(50809, container.Ports[8080]);
        Assert.Equal(50810, container.Ports[5432]);
        Assert.False(container.Ports.ContainsKey(9090));
        Assert.Equal("a=b", container.Environment["PASSWORD"]);
    }

    [Fact]
    public void ParseInspect_AnEmptyNetworkSettingsDoesNotHideTheTopLevelBindings()
    {
        var inspectOutput =
                """
                [
                    {
                        "Id": "container-id",
                        "Ports": { "8080/tcp": [ { "HostIp": "127.0.0.1", "HostPort": "50809" } ] },
                        "NetworkSettings": { "Ports": {} }
                    }
                ]
                """;

        Assert.Equal(50809, DockerContainerInfoParser.ParseInspectOutput(inspectOutput).Ports[8080]);
    }

    [Theory]
    [InlineData("1234 (dotnet) S 1 1234 1234 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 21 0 98765 12345 678", "98765")]
    [InlineData("1234 (my (weird) name) R 1 1234 1234 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 21 0 98765 12345 678", "98765")]
    [InlineData("1234 (dotnet) Z 1 1234 1234 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 21 0 98765 12345 678", null)]
    [InlineData("1234 (dotnet) S 1", null)]
    public void ParseStartTicks_ReadsTheStartTimeOfTheProcess(string stat, string? expected)
    {
        Assert.Equal(expected, SessionIdentity.ParseStartTicks(stat));
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

    [Fact]
    public void IsTransient_DockerApiResponse_ServerErrorIsTransient()
    {
        Assert.True(TransientError.IsTransient(ApiFailure(HttpStatusCode.ServiceUnavailable)));
        Assert.True(TransientError.IsTransient(ApiFailure(HttpStatusCode.InternalServerError)));
        Assert.True(TransientError.IsTransient(ApiFailure(HttpStatusCode.TooManyRequests)));
        Assert.True(TransientError.IsTransient(ApiFailure(HttpStatusCode.RequestTimeout)));
    }

    [Fact]
    public void IsTransient_DockerApiResponse_ClientErrorIsPermanent()
    {
        Assert.False(TransientError.IsTransient(ApiFailure(HttpStatusCode.NotFound)));
        Assert.False(TransientError.IsTransient(ApiFailure(HttpStatusCode.Unauthorized)));
        Assert.False(TransientError.IsTransient(ApiFailure(HttpStatusCode.Conflict)));
    }

    [Fact]
    public void IsTransient_InBandPullFailure_IsClassifiedFromItsMessage()
    {
        // The daemon reports a failed pull in the body of a response whose status is a success, so there is no status
        // code to classify: these are the messages the pull stream actually carries.
        Assert.True(TransientError.IsTransient(NoStatusCode("net/http: TLS handshake timeout")));
        Assert.True(TransientError.IsTransient(NoStatusCode("toomanyrequests: You have reached your pull rate limit.")));
        Assert.True(TransientError.IsTransient(NoStatusCode("received unexpected HTTP status: 503 Service Unavailable")));
        Assert.True(TransientError.IsTransient(NoStatusCode("unexpected EOF")));

        Assert.False(TransientError.IsTransient(NoStatusCode("manifest for busybox:nope not found: manifest unknown")));
        Assert.False(TransientError.IsTransient(NoStatusCode("pull access denied, repository does not exist")));

        static ContainerRuntimeException NoStatusCode(string message)
            => new("Unable to pull image 'busybox:1.37': " + message, ContainerRuntime.DockerApi, statusCode: null);
    }

    [Fact]
    public void IsTransient_CliFailure_IsClassifiedFromItsStandardError()
    {
        // Every CLI exits with the same code whatever the registry answered, so the standard error is the only clue.
        Assert.True(TransientError.IsTransient(CliFailure("Error response from daemon: toomanyrequests: too many requests")));
        Assert.True(TransientError.IsTransient(CliFailure("Error response from daemon: net/http: TLS handshake timeout")));

        Assert.False(TransientError.IsTransient(CliFailure("Error response from daemon: unauthorized: authentication required")));
        Assert.False(TransientError.IsTransient(CliFailure("Error response from daemon: manifest unknown")));

        static ContainerRuntimeException CliFailure(string standardError)
            => new("The command failed.", ContainerRuntime.Docker, "docker pull busybox:1.37", exitCode: 1, standardOutput: "", standardError);
    }

    [Fact]
    public void IsTransient_TransportFailures()
    {
        Assert.True(TransientError.IsTransient(new HttpRequestException("connection closed")));
        Assert.True(TransientError.IsTransient(new IOException("the socket was closed")));
        Assert.True(TransientError.IsTransient(new SocketException()));
        Assert.True(TransientError.IsTransient(new TimeoutException()));

        Assert.False(TransientError.IsTransient(new InvalidOperationException("boom")));
        Assert.False(TransientError.IsTransient(new NotSupportedException("boom")));
    }

    [Fact]
    public async Task RetryStrategy_ReturnsWithoutRetryingWhenTheFirstAttemptSucceeds()
    {
        var attempts = 0;
        var result = await RetryStrategy.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.FromResult("ok");
        }, TransientError.IsTransient, onRetry: null, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryStrategy_RetriesATransientFailureUntilItSucceeds()
    {
        var attempts = 0;
        var result = await RetryStrategy.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 2 ? throw new IOException("boom") : Task.FromResult("ok");
        }, TransientError.IsTransient, onRetry: null, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryStrategy_GivesUpAfterMaxAttemptsAndRethrowsTheLastFailure()
    {
        var attempts = 0;
        var exception = await Assert.ThrowsAsync<IOException>(() => RetryStrategy.ExecuteAsync(_ =>
        {
            attempts++;
            throw new IOException("boom " + attempts);
        }, TransientError.IsTransient, onRetry: null, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(RetryStrategy.MaxAttempts, attempts);
        Assert.Equal("boom 3", exception.Message);
    }

    [Fact]
    public async Task RetryStrategy_DoesNotRetryAPermanentFailure()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<ContainerRuntimeException>(() => RetryStrategy.ExecuteAsync(_ =>
        {
            attempts++;
            throw ApiFailure(HttpStatusCode.NotFound);
        }, TransientError.IsTransient, onRetry: null, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryStrategy_DoesNotRetryWhenTheCallerCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var attempts = 0;
        await Assert.ThrowsAsync<IOException>(() => RetryStrategy.ExecuteAsync(_ =>
        {
            attempts++;
            throw new IOException("boom");
        }, TransientError.IsTransient, onRetry: null, TimeSpan.Zero, cts.Token));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryStrategy_ReportsEveryRetry()
    {
        var reported = new List<int>();
        await Assert.ThrowsAsync<IOException>(() => RetryStrategy.ExecuteAsync(_ => throw new IOException("boom"),
            TransientError.IsTransient,
            (_, attempt, _) => reported.Add(attempt),
            TimeSpan.Zero,
            CancellationToken.None));

        Assert.Equal([1, 2], reported);
    }

    [Fact]
    public void RetryStrategy_DelayGrowsExponentiallyAndCarriesAJitter()
    {
        var baseDelay = TimeSpan.FromSeconds(1);
        foreach (var attempt in Enumerable.Range(1, RetryStrategy.MaxAttempts))
        {
            var backoff = baseDelay * Math.Pow(2, attempt - 1);
            var delay = RetryStrategy.GetDelay(attempt, baseDelay);

            Assert.InRange(delay, backoff, backoff * 1.25);
        }
    }

    /// <summary>Writes a CLI that exits with <paramref name="exitCode"/> whatever it is asked to do, so the outcome of the probe can be forced.</summary>
    /// <param name="exitCode">The exit code of every command.</param>
    /// <param name="failingCommand">A first argument for which the CLI fails with the exit code 1 instead.</param>
    private static string CreateStubCli(int exitCode, string? failingCommand = null)
    {
        var path = Path.Combine(Path.GetTempPath(), "MezTC-stub-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            path += ".cmd";
            var failing = failingCommand is null ? "" : $"@if \"%1\"==\"{failingCommand}\" exit /b 1\r\n";
            File.WriteAllText(path, $"{failing}@exit /b {exitCode}\r\n");
        }
        else
        {
            path += ".sh";
            var failing = failingCommand is null ? "" : $"if [ \"$1\" = \"{failingCommand}\" ]; then exit 1; fi\n";
            File.WriteAllText(path, $"#!/bin/sh\n{failing}exit {exitCode}\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static ContainerRuntimeException ApiFailure(HttpStatusCode statusCode)
        => new("boom", ContainerRuntime.DockerApi, statusCode);
}
