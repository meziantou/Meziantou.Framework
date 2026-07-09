using System.Net.Sockets;
using System.Text.Json;
using Meziantou.Extensions.Logging.Xunit.v3;
using Meziantou.Xunit;

namespace Meziantou.Framework.TemporaryContainers.Tests;

/// <summary>Integration tests shared by every runtime. One concrete subclass per runtime supplies the runtime to exercise; the whole class is skipped when that runtime is not installed.</summary>
public abstract class ContainerRuntimeTestsBase
{
    private const string LinuxImage = "busybox:1.37";
    private const string WindowsImage = "mcr.microsoft.com/windows/servercore:ltsc2022";
    private const string LinuxIndexFilePath = "/www/index.html";
    private const string LinuxTempDirectory = "/tmp";
    private const string WindowsIndexFilePath = "C:/www/index.html";
    private const string WindowsTempDirectory = "C:/tmp";
    private const string LinuxHttpServerCommand = "mkdir -p /www; printf 'hello from container' > /www/index.html; echo SERVER READY; exec httpd -f -p 8080 -h /www";
    private const string WindowsHttpServerCommand = "$content='hello from container'; New-Item -ItemType Directory -Path C:/www -Force | Out-Null; Set-Content -Path C:/www/index.html -Value $content -NoNewline; $listener=[System.Net.HttpListener]::new(); $listener.Prefixes.Add('http://+:8080/'); $listener.Start(); Write-Output 'SERVER READY'; while ($true) { $context=$listener.GetContext(); $bytes=[System.Text.Encoding]::UTF8.GetBytes($content); $context.Response.ContentLength64=$bytes.Length; $context.Response.OutputStream.Write($bytes, 0, $bytes.Length); $context.Response.OutputStream.Close(); }";

    private readonly bool _useWindowsContainerImages;

    private bool UseWindowsContainerImages => _useWindowsContainerImages;

    private string ContainerImage => UseWindowsContainerImages ? WindowsImage : LinuxImage;

    private string IndexFilePath => UseWindowsContainerImages ? WindowsIndexFilePath : LinuxIndexFilePath;

    private string TempDirectory => UseWindowsContainerImages ? WindowsTempDirectory : LinuxTempDirectory;

    private string DockerfileName => UseWindowsContainerImages ? "Dockerfile.windows" : "Dockerfile";

    protected ContainerRuntimeTestsBase(ContainerRuntime runtime)
    {
        Runtime = runtime;
        _useWindowsContainerImages = DetectUseWindowsContainerImages(runtime);

        if (TestEnvironment.IsOnGitHubActions())
        {
            if (OperatingSystem.IsWindows() && runtime is ContainerRuntime.Podman)
                global::Xunit.Assert.SkipUnless(ContainerRuntimeInfo.IsAvailable(runtime), $"The '{runtime}' container runtime is not available on this system.");

            if (OperatingSystem.IsMacOS())
                global::Xunit.Assert.SkipUnless(ContainerRuntimeInfo.IsAvailable(runtime), $"The '{runtime}' container runtime is not available on this system.");
        }

        if (runtime is ContainerRuntime.AppleContainer && !OperatingSystem.IsMacOS())
            global::Xunit.Assert.SkipUnless(ContainerRuntimeInfo.IsAvailable(runtime), $"The '{runtime}' container runtime is not available on this system.");


        if (runtime is ContainerRuntime.Wslc && !OperatingSystem.IsWindows())
            global::Xunit.Assert.SkipUnless(ContainerRuntimeInfo.IsAvailable(runtime), $"The '{runtime}' container runtime is not available on this system.");
    }

    protected ContainerRuntime Runtime { get; }

    private static bool DetectUseWindowsContainerImages(ContainerRuntime runtime)
    {
        if (runtime is ContainerRuntime.AppleContainer or ContainerRuntime.Wslc)
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
        var commandName = runtime switch
        {
            ContainerRuntime.Docker => "docker",
            ContainerRuntime.Podman => "podman",
            ContainerRuntime.Wslc => "wslc",
            _ => null,
        };

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
            executable = name;
            return true;
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
        using var logsCts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        logsCts.CancelAfter(TimeSpan.FromSeconds(30));
        await foreach (var log in container.GetLogsAsync(logsCts.Token))
        {
            if (log.Message.Contains("SERVER READY", StringComparison.Ordinal))
            {
                readyLineFound = true;
                break;
            }
        }

        Assert.True(readyLineFound);
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
                options.Command.Add("Get-Content -Raw C:/tmp/written.txt");
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
                    options.Command.Add("Get-Content -Raw C:/tmp/copied.txt");
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
    public async Task Lifecycle_RestartStopAndDelete()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        await container.RestartAsync(XunitCancellationToken);
        Assert.True(container.GetMappedPort(8080) > 0);

        await container.StopAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Exited, (await container.InspectAsync(XunitCancellationToken)).State);

        await container.DeleteAsync(XunitCancellationToken);
        Assert.False(await container.ExistsAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Reuse_AdoptsExistingContainer()
    {
        var reuseId = "meziantou-tc-test-" + Guid.NewGuid().ToString("N");
        string firstId;

        var firstDefinition = CreateHttpServerDefinition();
        firstDefinition.ReuseId = reuseId;
        await using (var first = await StartWithRetryAsync(firstDefinition))
        {
            firstId = first.Id;
        }

        try
        {
            var secondDefinition = CreateHttpServerDefinition();
            secondDefinition.ReuseId = reuseId;
            await using var second = secondDefinition.CreateContainer();
            await second.EnsureCreatedAsync(XunitCancellationToken);

            Assert.Equal(firstId, second.Id);
        }
        finally
        {
            var cleanupDefinition = CreateHttpServerDefinition();
            cleanupDefinition.ReuseId = reuseId;
            var cleanup = cleanupDefinition.CreateContainer();
            await cleanup.EnsureCreatedAsync(XunitCancellationToken);
            await cleanup.DeleteAsync(XunitCancellationToken);
            await cleanup.DisposeAsync();
        }
    }

    /// <summary>Shared pause/unpause assertion for runtimes that support it (called from the relevant subclasses).</summary>
    protected async Task AssertPauseUnpauseAsync()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());

        await container.PauseAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Paused, (await container.InspectAsync(XunitCancellationToken)).State);

        await container.UnpauseAsync(XunitCancellationToken);
        Assert.Equal(ContainerState.Running, (await container.InspectAsync(XunitCancellationToken)).State);
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

    private void AddHttpPortBinding(ContainerDefinition definition)
    {
        if (Runtime is ContainerRuntime.AppleContainer)
        {
            definition.Ports.Add(GetFreeTcpPort(), 8080);
        }
        else
        {
            definition.Ports.Add(8080);
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> GetStringWithRetryAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        const int MaxAttempts = 60;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.GetStringAsync(uri, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private async Task<string> GetIndexContentAsync(TemporaryContainer container)
    {
        if (Runtime is ContainerRuntime.AppleContainer)
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
        using var client = new HttpClient();
        return await GetStringWithRetryAsync(client, new Uri($"http://127.0.0.1:{port}/"), XunitCancellationToken);
    }

    protected static async Task<TemporaryContainer> StartWithRetryAsync(ContainerDefinition definition)
    {
        const int MaxRetries = 3;
        for (var i = 0; ; i++)
        {
            var container = definition.CreateContainer();
            try
            {
                await container.StartAsync(XunitCancellationToken);
                return container;
            }
            catch when (i < MaxRetries)
            {
                await container.DisposeAsync();
                await Task.Delay(1000, XunitCancellationToken);
            }
        }
    }
}
