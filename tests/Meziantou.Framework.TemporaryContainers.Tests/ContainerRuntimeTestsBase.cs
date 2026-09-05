using System.Net.Sockets;
using System.Text.Json;
using Meziantou.Extensions.Logging.Xunit.v3;
using Meziantou.Xunit;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Tests;

/// <summary>Integration tests shared by every runtime. One concrete subclass per runtime supplies the runtime to exercise; the whole class is skipped when that runtime is not available.</summary>
public abstract class ContainerRuntimeTestsBase : IAsyncLifetime
{
    private const string LinuxImage = "busybox:1.37";
    private const string WindowsImage = "mcr.microsoft.com/windows/servercore:ltsc2022";
    private const string LinuxIndexFilePath = "/www/index.html";
    private const string LinuxTempDirectory = "/tmp";
    private const string WindowsIndexFilePath = "C:/www/index.html";
    private const string WindowsTempDirectory = "C:/Windows/Temp";
    private const string LinuxHttpServerCommand = "mkdir -p /www; printf 'hello from container' > /www/index.html; echo SERVER READY; exec httpd -f -p 8080 -h /www";
    private const string WindowsHttpServerCommand = "$content='hello from container'; New-Item -ItemType Directory -Path C:/www -Force | Out-Null; Set-Content -Path C:/www/index.html -Value $content -NoNewline; $listener=[System.Net.HttpListener]::new(); $listener.Prefixes.Add('http://+:8080/'); $listener.Start(); Write-Output 'SERVER READY'; while ($true) { $context=$listener.GetContext(); $bytes=[System.Text.Encoding]::UTF8.GetBytes($content); $context.Response.ContentLength64=$bytes.Length; $context.Response.OutputStream.Write($bytes, 0, $bytes.Length); $context.Response.OutputStream.Close(); }";

    private bool _useWindowsContainerImages;

    private bool UseWindowsContainerImages => _useWindowsContainerImages;

    private string ContainerImage => UseWindowsContainerImages ? WindowsImage : LinuxImage;

    private string IndexFilePath => UseWindowsContainerImages ? WindowsIndexFilePath : LinuxIndexFilePath;

    private string TempDirectory => UseWindowsContainerImages ? WindowsTempDirectory : LinuxTempDirectory;

    private string DockerfileName => UseWindowsContainerImages ? "Dockerfile.windows" : "Dockerfile";

    protected ContainerRuntimeTestsBase(ContainerRuntime runtime)
    {
        Runtime = runtime;
    }

    protected ContainerRuntime Runtime { get; }

    /// <summary>Gets a value indicating whether the runtime must be available in the current environment. When it is, an unavailable runtime fails the tests instead of skipping them, so a broken setup cannot silently turn the suite into a no-op.</summary>
    protected virtual bool IsRuntimeRequired => false;

    /// <summary>Checking that the runtime answers is asynchronous, so the skip gate cannot live in the constructor.</summary>
    public async ValueTask InitializeAsync()
    {
        var isSupported = await Runtime.IsSupportedAsync(XunitCancellationToken);
        if (IsRuntimeRequired)
        {
            global::Xunit.Assert.True(isSupported, $"The '{Runtime}' container runtime must be available in this environment, but it did not answer.");
        }
        else
        {
            global::Xunit.Assert.SkipUnless(isSupported, $"The '{Runtime}' container runtime is not available on this system.");
        }

        _useWindowsContainerImages = DetectUseWindowsContainerImages(Runtime);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static bool DetectUseWindowsContainerImages(ContainerRuntime runtime)
    {
        if (runtime == ContainerRuntime.AppleContainer || runtime == ContainerRuntime.Wslc)
            return false;

        if (TryGetRuntimeContainerOs(runtime, out var containerOs))
            return string.Equals(containerOs, "windows", StringComparison.OrdinalIgnoreCase);

        return OperatingSystem.IsWindows();
    }

    private static bool TryGetRuntimeContainerOs(ContainerRuntime runtime, out string? os)
    {
        os = null;

        if (!TryGetRuntimeExecutable(runtime, out var executable))
            return false;

        if (!TryRunProbe(executable, ["info", "--format", "json"], out var result) || result.ExitCode != 0)
            return false;

        return TryGetContainerOsFromJson(result.StandardOutput, out os);
    }

    private static bool TryGetRuntimeExecutable(ContainerRuntime runtime, out string executable)
    {
        string? commandName;
        if (runtime == ContainerRuntime.Docker)
        {
            commandName = "docker";
        }
        else if (runtime == ContainerRuntime.Podman)
        {
            commandName = "podman";
        }
        else if (runtime == ContainerRuntime.Wslc)
        {
            commandName = "wslc";
        }
        else
        {
            commandName = null;
        }

        if (commandName is null)
        {
            executable = string.Empty;
            return false;
        }

        return TryFindExecutable(commandName, out executable);
    }

    private static bool TryFindExecutable(string name, out string executable)
    {
        if (Path.IsPathRooted(name) && File.Exists(name))
        {
            executable = string.Empty;
            return false;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IEnumerable<string> candidates = [name];

        if (OperatingSystem.IsWindows())
        {
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(extension => extension.StartsWith('.', StringComparison.Ordinal) ? extension : "." + extension);

            candidates = [.. candidates, .. extensions.Select(extension => name + extension)];
        }

        foreach (var entry in pathEntries)
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(entry, candidate);
                if (File.Exists(fullPath))
                {
                    executable = fullPath;
                    return true;
                }
            }
        }

        executable = name;
        return true;
    }

    private static bool TryRunProbe(string executable, string[] arguments, out (int ExitCode, string StandardOutput) result)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
            {
                result = default;
                return false;
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                result = default;
                return false;
            }

            result = (process.ExitCode, standardOutputTask.GetAwaiter().GetResult());
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static bool TryGetContainerOsFromJson(string json, out string? os)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (TryGetStringProperty(document.RootElement, "OSType", out os))
                return true;

            if (TryGetStringProperty(document.RootElement, "os", out os))
                return true;

            if (document.RootElement.TryGetProperty("host", out var hostElement) && TryGetStringProperty(hostElement, "os", out os))
                return true;
        }
        catch
        {
        }

        os = null;
        return false;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind is JsonValueKind.String)
                {
                    value = property.Value.GetString();
                    return !string.IsNullOrEmpty(value);
                }
            }
        }

        value = null;
        return false;
    }

    [Fact]
    public async Task StartAsync_ServesHttpAndReportsReady()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        var content = await GetIndexContentAsync(container);
        Assert.Equal("hello from container", content);

        var info = await container.InspectAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Running, info.State);
        Assert.True(await container.ExistsAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task DockerfileImage_BuildsAndServesHttp()
    {
        var imageDirectory = Path.Combine(AppContext.BaseDirectory, "TestImage");
        var definition = new ContainerDefinition(new DockerfileImage(Path.Combine(imageDirectory, DockerfileName), imageDirectory))
        {
            Runtime = Runtime,
        };
        AddHttpPortBinding(definition);
        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));
        definition.WaitStrategies.Add(Wait.ForPort(8080));
        definition.Logging.Logger = XUnitLogger.CreateLogger();


        await using var container = await StartWithRetryAsync(definition);

        var content = await GetIndexContentAsync(container);
        Assert.Equal("hello from container", content);
    }

    [Fact]
    public async Task ExecAsync_RunsCommandInContainer()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        var exec = await container.ExecAsync(options =>
        {
            if (UseWindowsContainerImages)
            {
                options.Command.Add("powershell");
                options.Command.Add("-NoProfile");
                options.Command.Add("-Command");
                options.Command.Add("Get-Content -Raw C:/www/index.html");
            }
            else
            {
                options.Command.Add("cat");
                options.Command.Add("/www/index.html");
            }
        }, XunitCancellationToken);

        Assert.Equal(0, exec.ExitCode);
        Assert.Contains("hello from container", exec.StandardOutput);
    }

    [Fact]
    public async Task GetLogsAsync_StreamsReadyMessage()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        var readyLineFound = false;
        var logs = new List<string>();
        using var logsCts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        logsCts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await foreach (var log in container.GetLogsAsync(logsCts.Token))
            {
                logs.Add(log.Message);
                if (log.Message.Contains("SERVER READY", StringComparison.Ordinal))
                {
                    readyLineFound = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!XunitCancellationToken.IsCancellationRequested)
        {
            // The timeout expired. The assertion below reports the logs received so far.
        }

        Assert.True(readyLineFound, "The logs do not contain 'SERVER READY':\n" + string.Join('\n', logs));
    }

    [Fact]
    public async Task Files_OpenReadWriteAndCopy()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        await using (var stream = await container.OpenReadAsync(IndexFilePath, XunitCancellationToken))
        using (var reader = new StreamReader(stream))
        {
            Assert.Equal("hello from container", await reader.ReadToEndAsync(XunitCancellationToken));
        }

        var writtenPath = TempDirectory + "/written.txt";
        using (var payload = new MemoryStream(Encoding.UTF8.GetBytes("written content")))
        {
            await container.WriteFileAsync(writtenPath, payload, XunitCancellationToken);
        }

        var writtenExec = await container.ExecAsync(options =>
        {
            if (UseWindowsContainerImages)
            {
                options.Command.Add("powershell");
                options.Command.Add("-NoProfile");
                options.Command.Add("-Command");
                options.Command.Add("Get-Content -Raw " + writtenPath);
            }
            else
            {
                options.Command.Add("cat");
                options.Command.Add("/tmp/written.txt");
            }
        }, XunitCancellationToken);
        Assert.Contains("written content", writtenExec.StandardOutput);

        var localFile = Path.Combine(Path.GetTempPath(), "MezTC-copy-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(localFile, "copied content", XunitCancellationToken);
        try
        {
            var copiedPath = TempDirectory + "/copied.txt";
            await container.CopyToContainerAsync(localFile, copiedPath, XunitCancellationToken);
            var copiedExec = await container.ExecAsync(options =>
            {
                if (UseWindowsContainerImages)
                {
                    options.Command.Add("powershell");
                    options.Command.Add("-NoProfile");
                    options.Command.Add("-Command");
                    options.Command.Add("Get-Content -Raw " + copiedPath);
                }
                else
                {
                    options.Command.Add("cat");
                    options.Command.Add("/tmp/copied.txt");
                }
            }, XunitCancellationToken);
            Assert.Contains("copied content", copiedExec.StandardOutput);
        }
        finally
        {
            File.Delete(localFile);
        }

        var downloaded = Path.Combine(Path.GetTempPath(), "MezTC-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            await container.CopyFromContainerAsync(IndexFilePath, downloaded, XunitCancellationToken);
            Assert.Equal("hello from container", await File.ReadAllTextAsync(downloaded, XunitCancellationToken));
        }
        finally
        {
            File.Delete(downloaded);
        }
    }

    [Fact]
    public Task Lifecycle_RestartStopAndDelete()
    {
        // Restarting a container makes the runtime rebuild its port forwarding, which fails transiently on CI agents
        // (rootless podman reports 'pasta failed with exit code 1: netlink: Unexpected sequence number'), so the whole
        // test is retried. Only container runtime failures are retried: an assertion failure still fails immediately.
        return ContainerTestHelper.RunWithRuntimeRetryAsync(async () =>
        {
            await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

            await container.RestartAsync(XunitCancellationToken);
            Assert.True(container.GetMappedPort(8080) > 0);

            await container.StopAsync(XunitCancellationToken);
            Assert.Equal(ContainerState.Exited, (await container.InspectAsync(XunitCancellationToken)).State);

            await container.DeleteAsync(XunitCancellationToken);
            Assert.False(await container.ExistsAsync(XunitCancellationToken));
        }, XunitCancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_RemovesTheContainerWhenTheLoggerThrows()
    {
        var definition = CreateHttpServerDefinition();
        definition.Logging.Logger = new ThrowingLogger();

        var container = await StartWithRetryAsync(definition);
        var id = container.Id;

        // Let the forwarding pump reach the logger, so the failure is in flight when the container is disposed.
        await Task.Delay(TimeSpan.FromSeconds(1), XunitCancellationToken);

        await container.DisposeAsync();

        Assert.False(await Runtime.ExistsAsync(id, XunitCancellationToken));
    }

    /// <summary>Mimics a logger backed by xunit's test output helper, which throws once the test that owns it has completed.</summary>
    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("There is no currently active test.");
    }

    [Fact]
    public async Task Reuse_AdoptsExistingContainer()
    {
        var reuseId = "meziantou-tc-test-" + Guid.NewGuid().ToString("N");
        string firstId;
        int firstPort;

        var firstDefinition = CreateHttpServerDefinition();
        firstDefinition.ReuseId = reuseId;
        await using (var first = await StartWithRetryAsync(firstDefinition))
        {
            firstId = first.Id;
            firstPort = first.GetMappedPort(8080);
        }

        try
        {
            var secondDefinition = CreateHttpServerDefinition();
            secondDefinition.ReuseId = reuseId;
            await using var second = secondDefinition.CreateContainer();
            await second.EnsureCreatedAsync(XunitCancellationToken);

            Assert.Equal(firstId, second.Id);

            // The adopted container keeps the host ports it was created with, so the second run has to report those
            // and not a freshly picked mapping.
            await second.StartAsync(XunitCancellationToken);
            Assert.Equal(firstPort, second.GetMappedPort(8080));
        }
        finally
        {
            await DeleteReusedContainerAsync(reuseId);
        }
    }

    /// <summary>Removes the container kept alive by <see cref="ContainerDefinition.ReuseId"/>. Failures are reported but never thrown, so the cleanup cannot hide the failure under test.</summary>
    private async Task DeleteReusedContainerAsync(string reuseId)
    {
        try
        {
            var cleanupDefinition = CreateHttpServerDefinition();
            cleanupDefinition.ReuseId = reuseId;
            var cleanup = cleanupDefinition.CreateContainer();
            await cleanup.EnsureCreatedAsync(XunitCancellationToken);
            await cleanup.DeleteAsync(XunitCancellationToken);
            await cleanup.DisposeAsync();
        }
        catch (Exception ex)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Failed to delete the container reused by '{reuseId}': {ex}");
        }
    }

    /// <summary>Shared assertion that a failing runtime command reports what the runtime printed (called from the
    /// relevant subclasses). It is not run for every runtime: wslc has no copy command, so the adapter streams the
    /// file itself and a missing source fails as a <see cref="FileNotFoundException"/> before any command runs.</summary>
    protected async Task AssertFailedCommandReportsWhatTheRuntimeComplainedAboutAsync()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        // The source is missing on the host, so the runtime rejects the copy before it touches the container.
        // Asking for a missing path *inside* the container is not equivalent: 'container copy' never returns for
        // apple/container, which hangs the test host rather than failing.
        var missingSource = Path.Combine(Path.GetTempPath(), "MezTC-missing-" + Guid.NewGuid().ToString("N"));
        var exception = await Assert.ThrowsAsync<ContainerRuntimeException>(async () =>
            await container.CopyToContainerAsync(missingSource, TempDirectory + "/copied.txt", XunitCancellationToken));

        Assert.Equal(Runtime, exception.Runtime);
        Assert.NotEqual(0, exception.ExitCode);
        Assert.NotNull(exception.Command);

        // The point of the exception: whatever the runtime printed has to reach the message, otherwise a CI failure
        // is nothing but an exit code.
        var reported = string.IsNullOrWhiteSpace(exception.StandardError) ? exception.StandardOutput : exception.StandardError;
        Assert.False(string.IsNullOrWhiteSpace(reported), "The runtime reported neither a standard error nor a standard output.");
        Assert.Contains(reported.Trim(), exception.Message);
        Assert.Contains(exception.Command, exception.Message);
    }

    /// <summary>Shared assertion that a container dying before its ready message fails with what it printed (called
    /// from the relevant subclasses). It is not run for every runtime: apple/container keeps the log stream open after
    /// the container exits, so the wait times out instead of ending.</summary>
    protected async Task AssertStartFailureReportsContainerOutputAsync()
    {
        var definition = new ContainerDefinition(new RegistryImage(ContainerImage))
        {
            Runtime = Runtime,
        };
        if (UseWindowsContainerImages)
        {
            definition.Command.Add("powershell");
            definition.Command.Add("-NoProfile");
            definition.Command.Add("-Command");
            definition.Command.Add("Write-Output 'the entrypoint gave up'; exit 3");
        }
        else
        {
            definition.Command.Add("sh");
            definition.Command.Add("-c");
            definition.Command.Add("echo 'the entrypoint gave up'; exit 3");
        }

        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));

        await using var container = definition.CreateContainer();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await container.StartAsync(XunitCancellationToken));

        // A container that dies during startup is the common case, and what it printed on the way out is the only
        // thing that explains why. Reporting nothing but the missing pattern leaves a CI failure undiagnosable.
        Assert.Contains("the entrypoint gave up", exception.Message);
        Assert.Contains(ContainerState.Exited.ToString(), exception.Message);
        Assert.Contains("exit code 3", exception.Message);
    }

    /// <summary>Shared pause/unpause assertion for runtimes that support it (called from the relevant subclasses).</summary>
    protected async Task AssertPauseUnpauseAsync()
    {
        global::Xunit.Assert.SkipUnless(!UseWindowsContainerImages || Runtime != ContainerRuntime.Docker, "docker pause is not implemented for Windows process-isolated containers");

        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        await container.PauseAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Paused, (await container.InspectAsync(XunitCancellationToken)).State);

        await container.UnpauseAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Running, (await container.InspectAsync(XunitCancellationToken)).State);
    }

    /// <summary>Shared assertion that the library creates the volume a container mounts, that the volume outlives the
    /// container, and that it is removed on demand (called from the runtimes that support volumes).</summary>
    protected Task AssertVolumeLifecycleAsync()
    {
        return ContainerTestHelper.RunWithRuntimeRetryAsync(async () =>
        {
            var mountPath = UseWindowsContainerImages ? "C:/data" : "/data";
            var filePath = mountPath + "/payload.txt";

            // The volume is declared first so it is disposed last: a runtime refuses to remove a volume a container
            // still references, and that failure would be swallowed by the best-effort cleanup.
            await using var volume = new VolumeDefinition { Runtime = Runtime }.CreateVolume();
            Assert.False(await volume.ExistsAsync(XunitCancellationToken));

            var definition = CreateHttpServerDefinition();
            definition.Mounts.AddVolume(volume, mountPath);
            await using (var container = await StartWithRetryAsync(definition))
            {
                // Starting the container is what creates the volume: nothing called EnsureCreatedAsync.
                Assert.True(await volume.ExistsAsync(XunitCancellationToken), "Starting the container did not create the volume it mounts.");

                await WriteThroughMountAsync(container, filePath, "content written through the mount");
                Assert.Equal("content written through the mount", await ReadThroughMountAsync(container, filePath));
            }

            Assert.True(await volume.ExistsAsync(XunitCancellationToken), "The volume did not survive the container that mounted it.");

            await volume.DeleteAsync(XunitCancellationToken);
            Assert.False(await volume.ExistsAsync(XunitCancellationToken), "The volume was not removed.");
        }, XunitCancellationToken);
    }

    /// <summary>Shared assertion that a volume carries its content from one container to the next (called from the
    /// runtimes that support it). Apple's <c>container</c> 1.1.0 hangs on any operation against a container that
    /// mounts a volume a deleted container used, so this is not run for every runtime.</summary>
    protected Task AssertVolumeSharedBetweenContainersAsync()
    {
        return ContainerTestHelper.RunWithRuntimeRetryAsync(async () =>
        {
            var mountPath = UseWindowsContainerImages ? "C:/data" : "/data";
            var filePath = mountPath + "/payload.txt";

            await using var volume = new VolumeDefinition { Runtime = Runtime }.CreateVolume();

            var writerDefinition = CreateHttpServerDefinition();
            writerDefinition.Mounts.AddVolume(volume, mountPath);
            await using (var writer = await StartWithRetryAsync(writerDefinition))
            {
                await WriteThroughMountAsync(writer, filePath, "content from the first container");
            }

            var readerDefinition = CreateHttpServerDefinition();
            readerDefinition.Mounts.AddVolume(volume, mountPath);
            await using (var reader = await StartWithRetryAsync(readerDefinition))
            {
                Assert.Equal("content from the first container", await ReadThroughMountAsync(reader, filePath));
            }
        }, XunitCancellationToken);
    }

    /// <summary>Writes a file inside a mount from within the container. The copy commands cannot be used for this:
    /// on Windows containers <c>docker cp</c> writes into the container's own layer instead of the mounted volume, so
    /// the content never reaches the volume.</summary>
    private async Task WriteThroughMountAsync(TemporaryContainer container, string path, string content)
    {
        var exec = await container.ExecAsync(options =>
        {
            if (UseWindowsContainerImages)
            {
                options.Command.Add("powershell");
                options.Command.Add("-NoProfile");
                options.Command.Add("-Command");
                options.Command.Add($"Set-Content -Path {path} -Value '{content}' -NoNewline");
            }
            else
            {
                options.Command.Add("sh");
                options.Command.Add("-c");
                options.Command.Add($"printf %s '{content}' > {path}");
            }
        }, XunitCancellationToken);

        Assert.Equal(0, exec.ExitCode);
    }

    /// <summary>Reads a file inside a mount from within the container. See <see cref="WriteThroughMountAsync"/> for why the copy commands are not used.</summary>
    private async Task<string> ReadThroughMountAsync(TemporaryContainer container, string path)
    {
        var exec = await container.ExecAsync(options =>
        {
            if (UseWindowsContainerImages)
            {
                options.Command.Add("powershell");
                options.Command.Add("-NoProfile");
                options.Command.Add("-Command");
                options.Command.Add($"Get-Content -Raw {path}");
            }
            else
            {
                options.Command.Add("cat");
                options.Command.Add(path);
            }
        }, XunitCancellationToken);

        Assert.Equal(0, exec.ExitCode);
        return exec.StandardOutput.Trim();
    }

    /// <summary>Shared assertion that a read-only volume mount rejects writes (called from the runtimes that support volumes).</summary>
    protected Task AssertReadOnlyVolumeMountAsync()
    {
        return ContainerTestHelper.RunWithRuntimeRetryAsync(async () =>
        {
            var mountPath = UseWindowsContainerImages ? "C:/data" : "/data";

            await using var volume = new VolumeDefinition { Runtime = Runtime }.CreateVolume();

            var definition = CreateHttpServerDefinition();
            definition.Mounts.AddVolume(volume, mountPath, readOnly: true);
            await using var container = await StartWithRetryAsync(definition);

            var exec = await container.ExecAsync(options =>
            {
                if (UseWindowsContainerImages)
                {
                    options.Command.Add("powershell");
                    options.Command.Add("-NoProfile");
                    options.Command.Add("-Command");
                    options.Command.Add($"Set-Content -Path {mountPath}/denied.txt -Value 'nope'");
                }
                else
                {
                    options.Command.Add("sh");
                    options.Command.Add("-c");
                    options.Command.Add($"echo nope > {mountPath}/denied.txt");
                }
            }, XunitCancellationToken);

            Assert.NotEqual(0, exec.ExitCode);
        }, XunitCancellationToken);
    }

    protected ContainerDefinition CreateHttpServerDefinition()
    {
        var definition = new ContainerDefinition(new RegistryImage(ContainerImage))
        {
            Runtime = Runtime,
        };
        if (UseWindowsContainerImages)
        {
            definition.Command.Add("powershell");
            definition.Command.Add("-NoProfile");
            definition.Command.Add("-Command");
            definition.Command.Add(WindowsHttpServerCommand);
        }
        else
        {
            definition.Command.Add("sh");
            definition.Command.Add("-c");
            definition.Command.Add(LinuxHttpServerCommand);
        }
        AddHttpPortBinding(definition);
        definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));
        definition.WaitStrategies.Add(Wait.ForPort(8080));
        definition.Logging.Logger = XUnitLogger.CreateLogger();
        return definition;
    }

    private static void AddHttpPortBinding(ContainerDefinition definition)
    {
        definition.Ports.Add(8080);
    }

    /// <summary>Polls the container until it serves a response. A container that just reported readiness may still refuse connections for a short while, and the connection itself may be reset while the runtime sets up the port forwarding.</summary>
    private static async Task<string> GetStringWithRetryAsync(Uri uri, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(60);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        Exception? lastFailure = null;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                return await client.GetStringAsync(uri, cts.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                lastFailure = ex;

                // The test cancellation token is used on purpose: the delay must not be interrupted when the timeout expires.
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"'{uri}' did not serve a response within {timeout}.", lastFailure);
    }

    [Fact]
    public async Task TwoContainers_SameExposedPort_GetDifferentMappedPorts()
    {
        await using var container1 = await StartWithRetryAsync(CreateHttpServerDefinition());
        await using var container2 = await StartWithRetryAsync(CreateHttpServerDefinition());

        var port1 = container1.GetMappedPort(8080);
        var port2 = container2.GetMappedPort(8080);

        Assert.True(port1 > 0);
        Assert.True(port2 > 0);
        Assert.NotEqual(port1, port2);
    }

    private async Task<string> GetIndexContentAsync(TemporaryContainer container)
    {
        if (Runtime == ContainerRuntime.AppleContainer)
        {
            var exec = await container.ExecAsync(options =>
            {
                options.Command.Add("sh");
                options.Command.Add("-c");
                options.Command.Add("wget -qO- http://127.0.0.1:8080/");
            }, XunitCancellationToken);

            Assert.Equal(0, exec.ExitCode);
            return exec.StandardOutput.Trim();
        }

        var port = container.GetMappedPort(8080);
        return await GetStringWithRetryAsync(new Uri($"http://127.0.0.1:{port}/"), XunitCancellationToken);
    }

    protected static Task<TemporaryContainer> StartWithRetryAsync(ContainerDefinition definition)
    {
        return ContainerTestHelper.StartWithRetryAsync(definition, XunitCancellationToken);
    }
}
