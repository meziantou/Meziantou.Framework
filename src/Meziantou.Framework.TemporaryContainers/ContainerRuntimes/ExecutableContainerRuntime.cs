using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Base runtime backed by an executable CLI.</summary>
internal abstract class ExecutableContainerRuntime : ContainerRuntime
{
    // A daemon that is reachable but busy can take a while to answer. The timeout only matters when it never answers.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly string? _executablePath;
    private readonly Lock _syncObject = new();
    private ContainerCli? _cli;
    private volatile string? _probeOutput;
    private Task<string?>? _probe;

    /// <param name="executablePath">When set, the CLI to run instead of looking <see cref="ExecutableName"/> up in the PATH.</param>
    protected ExecutableContainerRuntime(string name, string? executablePath = null)
        : base(name)
    {
        _executablePath = executablePath;
    }

    private protected ContainerCli Cli
    {
        get
        {
            return EnsureCliInitialized() ?? throw CreateUnavailableRuntimeException(this);
        }
    }

    internal abstract string ExecutableName { get; }

    internal virtual bool SupportsPause => false;

    internal virtual bool SupportsRestart => false;

    /// <summary>Whether the CLI reads environment variables from a file, which keeps their values off the command line.</summary>
    internal virtual bool SupportsEnvironmentFile => true;

    public override async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        if (EnsureCliInitialized() is not { } cli)
            return false;

        return await GetProbeOutputAsync(cli, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>Arguments of a cheap command that succeeds only when the daemon behind the CLI answers.</summary>
    internal abstract IReadOnlyList<string> BuildProbeArguments();

    /// <summary>The standard output of the probe that succeeded, which some runtimes use to learn about their daemon. <see langword="null"/> until the runtime is known to be operational.</summary>
    private protected string? ProbeOutput => _probeOutput;

    internal string? FindExecutable()
    {
        if (_executablePath is not null)
            return _executablePath;

        // On Windows, Docker Desktop ships both an extensionless shim and the real '.exe';
        // the shim cannot be launched by Process.Start, so prefer an executable extension.
        if (OperatingSystem.IsWindows())
        {
            foreach (var extension in (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ExecutableFinder.GetFullExecutablePath(ExecutableName + extension) is { } withExtension)
                    return withExtension;
            }
        }

        return ExecutableFinder.GetFullExecutablePath(ExecutableName);
    }

    private ContainerCli? EnsureCliInitialized()
    {
        if (_cli is { } cli)
            return cli;

        lock (_syncObject)
        {
            if (_cli is { } existingCli)
                return existingCli;

            if (FindExecutable() is not { } executable)
                return null;

            _cli = new ContainerCli(this, executable);
            return _cli;
        }
    }

    /// <summary>Checks that the daemon behind the CLI answers. Finding the executable is not enough: Docker Desktop leaves 'docker' on the PATH when the engine is stopped, and every command then fails with a connection error.</summary>
    private async Task<string?> GetProbeOutputAsync(ContainerCli cli, CancellationToken cancellationToken)
    {
        if (_probeOutput is { } output)
            return output;

        Task<string?> probe;
        lock (_syncObject)
        {
            if (_probeOutput is { } publishedOutput)
                return publishedOutput;

            // Concurrent callers share the probe in flight, so a burst of tests starting at once runs one process
            // instead of one each. Only a success is kept: a probe that completed without publishing its output failed,
            // and a daemon started after it is still detected by the next caller.
            if (_probe is null || _probe.IsCompleted)
                _probe = RunProbeAsync(cli);

            probe = _probe;
        }

        return await probe.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RunProbeAsync(ContainerCli cli)
    {
        using var cancellationTokenSource = new CancellationTokenSource(ProbeTimeout);
        try
        {
            var result = await cli.RunBufferedAsync(BuildProbeArguments(), cancellationTokenSource.Token, allowNonZero: true).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return null;

            _probeOutput = result.StandardOutput;
            return result.StandardOutput;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            // The probe is a diagnostic: whatever prevents it from completing, including the timeout, means the runtime cannot be used.
            return null;
        }
    }

    /// <summary>Tells a command that failed because its target is missing from one that failed because the daemon did not answer: the probe only succeeds in the first case.</summary>
    private async Task EnsureDaemonAnswersAsync(CliResult failure, IReadOnlyList<string> failedArguments, CancellationToken cancellationToken)
    {
        var probe = await Cli.RunBufferedAsync(BuildProbeArguments(), cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (probe.ExitCode != 0)
            throw Cli.CreateFailure(failedArguments, failure);
    }

    internal abstract Task<string> PrepareImageAsync(ContainerDefinition definition, CancellationToken cancellationToken);

    /// <summary>Runs the pull command of the runtime, running it again when the registry fails for a reason that a second attempt can resolve.</summary>
    /// <remarks>A pull is idempotent, and the layers that were already fetched are cached by the runtime, so an attempt that follows a failure resumes instead of downloading everything again.</remarks>
    private protected async Task PullImageAsync(IReadOnlyList<string> args, string imageName, ILogger? logger, CancellationToken cancellationToken)
    {
        await RetryStrategy.ExecuteAsync(
            async ct => await Cli.RunBufferedAsync(args, ct).ConfigureAwait(false),
            TransientError.IsTransient,
            Log.CreateImagePullRetryCallback(logger, imageName),
            RetryStrategy.DefaultBaseDelay,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Makes sure a registry image is present before the container is created, so a pull goes through the retries instead of failing inside the create command.</summary>
    private protected async Task EnsureRegistryImageAsync(IReadOnlyList<string> inspectArguments, IReadOnlyList<string> pullArguments, string imageName, PullPolicy pullPolicy, ILogger? logger, CancellationToken cancellationToken)
    {
        if (pullPolicy is PullPolicy.Always)
        {
            await PullImageAsync(pullArguments, imageName, logger, cancellationToken).ConfigureAwait(false);
            return;
        }

        var inspect = await Cli.RunBufferedAsync(inspectArguments, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (inspect.ExitCode == 0)
            return;

        if (pullPolicy is PullPolicy.Never)
            throw new InvalidOperationException($"The image '{imageName}' is not present and the pull policy is '{nameof(PullPolicy.Never)}'.");

        await PullImageAsync(pullArguments, imageName, logger, cancellationToken).ConfigureAwait(false);
    }

    internal abstract Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken);

    internal abstract IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef, EnvironmentFile? environmentFile = null);

    internal abstract IReadOnlyList<string> BuildStartArguments(string id);

    internal abstract IReadOnlyList<string> BuildStopArguments(string id);

    internal virtual IReadOnlyList<string> BuildRestartArguments(string id) => throw new NotSupportedException($"The '{this}' runtime does not support restart.");

    internal virtual IReadOnlyList<string> BuildPauseArguments(string id) => throw new NotSupportedException($"The '{this}' runtime does not support pausing containers.");

    internal virtual IReadOnlyList<string> BuildUnpauseArguments(string id) => throw new NotSupportedException($"The '{this}' runtime does not support pausing containers.");

    internal abstract IReadOnlyList<string> BuildKillArguments(string id);

    internal abstract IReadOnlyList<string> BuildRemoveArguments(string id);

    internal abstract IReadOnlyList<string> BuildExistsArguments(string id);

    internal abstract IReadOnlyList<string> BuildInspectArguments(string id);

    internal abstract IReadOnlyList<string> BuildLogsArguments(string id, bool follow = true);

    internal abstract IReadOnlyList<string> BuildExecArguments(string id, ExecOptions options, EnvironmentFile? environmentFile = null);

    internal abstract IReadOnlyList<string> BuildCopyToContainerArguments(string id, string source, string destination);

    internal abstract IReadOnlyList<string> BuildCopyFromContainerArguments(string id, string source, string destination);

    internal abstract IReadOnlyList<string> BuildDeleteImageArguments(string image);

    internal abstract ContainerInfo ParseInspect(string output);

    internal virtual IReadOnlyList<string> BuildCreateVolumeArguments(VolumeDefinition definition, string name, string? instanceId = null)
        => throw CreateVolumesNotSupportedException();

    internal virtual IReadOnlyList<string> BuildDeleteVolumeArguments(string name)
        => throw CreateVolumesNotSupportedException();

    internal virtual IReadOnlyList<string> BuildVolumeExistsArguments(string name)
        => throw CreateVolumesNotSupportedException();

    internal virtual IReadOnlyDictionary<string, string>? ParseVolumeLabels(string output)
        => throw CreateVolumesNotSupportedException();

    private NotSupportedException CreateVolumesNotSupportedException()
        => new($"The '{this}' runtime does not support volumes.");

    /// <summary>Last chance to adjust what the container is created with, right before it is. Not called when an existing container is adopted through <see cref="ContainerDefinition.ReuseId"/>.</summary>
    internal virtual void PrepareDefinitionForCreate(ContainerDefinition definition)
    {
    }

    internal override Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        return EnsureCreatedCoreAsync(definition, cancellationToken);
    }

    private async Task<string> EnsureCreatedCoreAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.ReuseId is { } reuseId &&
            await FindReusableContainerAsync(reuseId, cancellationToken).ConfigureAwait(false) is { } existingId)
        {
            return existingId;
        }

        var imageRef = await PrepareImageAsync(definition, cancellationToken).ConfigureAwait(false);

        // After the image is ready, which can take minutes: a runtime that picks host ports itself must pick them as
        // late as possible, so no other process takes them in the meantime.
        PrepareDefinitionForCreate(definition);

        using var environmentFile = SupportsEnvironmentFile ? EnvironmentFile.Create(definition.Environment) : null;
        var args = BuildCreateArguments(definition, imageRef, environmentFile);
        try
        {
            var createResult = await Cli.RunBufferedAsync(args, cancellationToken).ConfigureAwait(false);
            return createResult.StandardOutput.Trim();
        }
        catch (ContainerRuntimeException) when (definition.ReuseId is not null)
        {
            // Another process created the container between the lookup and the creation, and the runtime refused the
            // name it already uses. Adopting it is the whole point of a reuse identifier.
            if (await FindReusableContainerAsync(definition.ReuseId, cancellationToken).ConfigureAwait(false) is { } concurrentId)
                return concurrentId;

            throw;
        }
    }

    internal override async Task StartAsync(string id, CancellationToken cancellationToken)
    {
        await Cli.RunBufferedAsync(BuildStartArguments(id), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task StopAsync(string id, CancellationToken cancellationToken)
    {
        await Cli.RunBufferedAsync(BuildStopArguments(id), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task RestartAsync(string id, CancellationToken cancellationToken)
    {
        if (SupportsRestart)
        {
            await Cli.RunBufferedAsync(BuildRestartArguments(id), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Cli.RunBufferedAsync(BuildStopArguments(id), cancellationToken).ConfigureAwait(false);
            await Cli.RunBufferedAsync(BuildStartArguments(id), cancellationToken).ConfigureAwait(false);
        }
    }

    internal override async Task PauseAsync(string id, CancellationToken cancellationToken)
    {
        if (!SupportsPause)
            throw new NotSupportedException($"The '{this}' runtime does not support pausing containers.");

        await Cli.RunBufferedAsync(BuildPauseArguments(id), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task UnpauseAsync(string id, CancellationToken cancellationToken)
    {
        if (!SupportsPause)
            throw new NotSupportedException($"The '{this}' runtime does not support pausing containers.");

        await Cli.RunBufferedAsync(BuildUnpauseArguments(id), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task KillAsync(string id, CancellationToken cancellationToken)
    {
        await Cli.RunBufferedAsync(BuildKillArguments(id), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var args = BuildRemoveArguments(id);
        var result = await Cli.RunBufferedAsync(args, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode == 0)
            return;

        // The runtimes fail the same way for a container that is already gone, which is not an error, and for one they
        // could not remove, which is. Whether the container is still there tells them apart.
        if (await ExistsAsync(id, cancellationToken).ConfigureAwait(false))
            throw Cli.CreateFailure(args, result);
    }

    internal override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        var args = BuildExistsArguments(id);
        var result = await Cli.RunBufferedAsync(args, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode == 0)
            return true;

        await EnsureDaemonAnswersAsync(result, args, cancellationToken).ConfigureAwait(false);
        return false;
    }

    internal override async Task CreateVolumeAsync(VolumeDefinition definition, string name, string instanceId, CancellationToken cancellationToken)
    {
        await Cli.RunBufferedAsync(BuildCreateVolumeArguments(definition, name, instanceId), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task DeleteVolumeAsync(string name, CancellationToken cancellationToken)
    {
        // A volume still in use is not removed, which the callers detect by probing the volume again.
        await Cli.RunBufferedAsync(BuildDeleteVolumeArguments(name), cancellationToken, allowNonZero: true).ConfigureAwait(false);
    }

    internal override async Task<bool> VolumeExistsAsync(string name, CancellationToken cancellationToken)
        => await GetVolumeLabelsAsync(name, cancellationToken).ConfigureAwait(false) is not null;

    internal override async Task<IReadOnlyDictionary<string, string>?> GetVolumeLabelsAsync(string name, CancellationToken cancellationToken)
    {
        var args = BuildVolumeExistsArguments(name);
        var result = await Cli.RunBufferedAsync(args, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode == 0)
            return ParseVolumeLabels(result.StandardOutput) ?? new Dictionary<string, string>(StringComparer.Ordinal);

        await EnsureDaemonAnswersAsync(result, args, cancellationToken).ConfigureAwait(false);
        return null;
    }

    internal override async Task DeleteImageAsync(string image, CancellationToken cancellationToken)
    {
        var args = BuildDeleteImageArguments(image);
        var result = await Cli.RunBufferedAsync(args, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode != 0)
            await EnsureDaemonAnswersAsync(result, args, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(BuildInspectArguments(id), cancellationToken).ConfigureAwait(false);
        return ParseInspect(result.StandardOutput);
    }

    internal override async IAsyncEnumerable<LogEntry> GetLogsAsync(string id, bool follow, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var instance = Cli.ExecuteStreaming(
            BuildLogsArguments(id, follow),
            line => channel.Writer.TryWrite(ParseLog(line, LogStream.Stdout, LogsIncludeTimestamps)),
            line => channel.Writer.TryWrite(ParseLog(line, LogStream.Stderr, LogsIncludeTimestamps)),
            cancellationToken);

        var completion = CompleteChannelWhenDoneAsync(instance, channel.Writer);
        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return entry;
        }
        finally
        {
            try
            {
                instance.Kill();
            }
            catch
            {
            }

            await completion.ConfigureAwait(false);
        }
    }

    internal override async Task<ExecResult> ExecAsync(string id, ExecOptions options, CancellationToken cancellationToken)
    {
        using var environmentFile = SupportsEnvironmentFile ? EnvironmentFile.Create(options.Environment) : null;
        var args = BuildExecArguments(id, options, environmentFile);
        var result = await Cli.RunBufferedAsync(args, cancellationToken, allowNonZero: true, input: options.StandardInput).ConfigureAwait(false);
        return new ExecResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    internal override async Task<Stream> OpenReadAsync(string id, string path, CancellationToken cancellationToken)
    {
        ContainerRuntimeException catFailure;
        try
        {
            return await OpenReadUsingExecAsync(id, ["cat", path], cancellationToken).ConfigureAwait(false);
        }
        catch (ContainerRuntimeException ex) when (IsReportedByCat(ex))
        {
            // 'cat' ran and could not read the file: there is nothing another way of reading it can fix.
            throw;
        }
        catch (ContainerRuntimeException ex)
        {
            // 'cat' could not run at all, as in a Windows container.
            catFailure = ex;
        }

        try
        {
            return await OpenReadUsingExecAsync(id,
            [
                "powershell",
                "-NoProfile",
                "-Command",
                "$bytes=[System.IO.File]::ReadAllBytes('" + EscapePowerShellSingleQuotedString(path) + "'); [Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length)",
            ], cancellationToken).ConfigureAwait(false);
        }
        catch (ContainerRuntimeException)
        {
        }

        try
        {
            return await OpenReadUsingCopyAsync(id, path, cancellationToken).ConfigureAwait(false);
        }
        catch (ContainerRuntimeException copyFailure)
        {
            throw new ContainerRuntimeException($"Unable to read '{path}' from the container. " + catFailure.Message, copyFailure);
        }
    }

    /// <summary>Both GNU and BusyBox <c>cat</c> prefix their errors with their name, while a runtime that cannot start the command reports it in words of its own.</summary>
    private static bool IsReportedByCat(ContainerRuntimeException exception)
        => exception.StandardError is { } standardError && standardError.TrimStart().StartsWith("cat:", StringComparison.Ordinal);

    private async Task<Stream> OpenReadUsingExecAsync(string id, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        var options = new ExecOptions();
        foreach (var item in command)
            options.Command.Add(item);

        var args = BuildExecArguments(id, options);
        var stream = new MemoryStream();
        try
        {
            await Cli.RunToStreamAsync(args, stream, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<Stream> OpenReadUsingCopyAsync(string id, string path, CancellationToken cancellationToken)
    {
        // The runtime creates the copy itself, with the permissions the file has in the container, so it is created in a
        // directory only the current user can enter.
        var directory = PrivateTemporaryFile.CreateDirectory();
        try
        {
            var tempFile = Path.Combine(directory, "content");
            await Cli.RunBufferedAsync(BuildCopyFromContainerArguments(id, path, tempFile), cancellationToken).ConfigureAwait(false);
            return new TemporaryFileStream(tempFile, directory);
        }
        catch
        {
            PrivateTemporaryFile.DeleteDirectory(directory);
            throw;
        }
    }

    internal override async Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
    {
        var tempFile = PrivateTemporaryFile.CreatePath();
        try
        {
            await using (var fileStream = PrivateTemporaryFile.Create(tempFile))
                await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

            await CopyToContainerAsync(id, tempFile, path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    internal override async Task CopyToContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        await Cli.RunBufferedAsync(BuildCopyToContainerArguments(id, source, destination), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task CopyFromContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        await Cli.RunBufferedAsync(BuildCopyFromContainerArguments(id, source, destination), cancellationToken).ConfigureAwait(false);
    }

    private static async Task CompleteChannelWhenDoneAsync(ProcessInstance instance, ChannelWriter<LogEntry> writer)
    {
        try
        {
            await instance.ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private static LogEntry ParseLog(string line, LogStream stream, bool includeTimestamps)
    {
        if (includeTimestamps)
        {
            var spaceIndex = line.IndexOf(' ', StringComparison.Ordinal);
            if (spaceIndex > 0 &&
                DateTimeOffset.TryParse(line[..spaceIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                return new LogEntry(stream, line[(spaceIndex + 1)..], timestamp);
            }
        }

        return new LogEntry(stream, line, Timestamp: null);
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
