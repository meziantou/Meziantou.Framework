using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Base runtime backed by an executable CLI.</summary>
internal abstract class ExecutableContainerRuntime : ContainerRuntime
{
    private readonly ContainerCli? _cli;

    protected ExecutableContainerRuntime(string name)
        : base(name)
    {
    }

    protected ExecutableContainerRuntime(string name, string executable, ILogger? logger)
        : base(name)
    {
        _cli = new ContainerCli(this, executable, logger);
    }

    private protected ContainerCli Cli => _cli ?? throw new InvalidOperationException($"The runtime '{this}' is not bound to an executable.");

    internal abstract string ExecutableName { get; }

    internal override bool IsSupported(ILogger? logger) => FindExecutable() is not null;

    internal override ContainerRuntime? TryResolve(ILogger? logger)
    {
        var exe = FindExecutable();
        return exe is not null ? Bind(exe, logger) : null;
    }

    internal string? FindExecutable()
    {
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

    internal abstract Task<string> PrepareImageAsync(ImageSource source, PullPolicy pullPolicy, CancellationToken cancellationToken);

    internal abstract Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken);

    internal abstract IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef);

    internal abstract IReadOnlyList<string> BuildStartArguments(string id);

    internal abstract IReadOnlyList<string> BuildStopArguments(string id);

    internal abstract IReadOnlyList<string> BuildRestartArguments(string id);

    internal abstract IReadOnlyList<string> BuildPauseArguments(string id);

    internal abstract IReadOnlyList<string> BuildUnpauseArguments(string id);

    internal abstract IReadOnlyList<string> BuildKillArguments(string id);

    internal abstract IReadOnlyList<string> BuildRemoveArguments(string id);

    internal abstract IReadOnlyList<string> BuildExistsArguments(string id);

    internal abstract IReadOnlyList<string> BuildInspectArguments(string id);

    internal virtual bool LogsIncludeTimestamps => false;

    internal abstract IReadOnlyList<string> BuildLogsArguments(string id);

    internal abstract IReadOnlyList<string> BuildExecArguments(string id, ExecOptions options);

    internal abstract IReadOnlyList<string> BuildCopyToContainerArguments(string id, string source, string destination);

    internal abstract IReadOnlyList<string> BuildCopyFromContainerArguments(string id, string source, string destination);

    internal abstract ContainerInfo ParseInspect(string output);

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

        var imageRef = await PrepareImageAsync(definition.Image, definition.PullPolicy, cancellationToken).ConfigureAwait(false);
        var args = BuildCreateArguments(definition, imageRef);
        var createResult = await Cli.RunBufferedAsync(args, cancellationToken).ConfigureAwait(false);
        return createResult.StandardOutput.Trim();
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
            await Cli.RunBufferedAsync(BuildRestartArguments(id), cancellationToken).ConfigureAwait(false);
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
        await Cli.RunBufferedAsync(BuildRemoveArguments(id), cancellationToken, allowNonZero: true).ConfigureAwait(false);
    }

    internal override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(BuildExistsArguments(id), cancellationToken, allowNonZero: true).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    internal override async Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(BuildInspectArguments(id), cancellationToken).ConfigureAwait(false);
        return ParseInspect(result.StandardOutput);
    }

    internal override async IAsyncEnumerable<LogEntry> GetLogsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var instance = Cli.ExecuteStreaming(
            BuildLogsArguments(id),
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
        var args = BuildExecArguments(id, options);
        var result = await Cli.RunBufferedAsync(args, cancellationToken, allowNonZero: true, input: options.StandardInput).ConfigureAwait(false);
        return new ExecResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    internal override async Task<Stream> OpenReadAsync(string id, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await OpenReadUsingExecAsync(id, ["cat", path], cancellationToken).ConfigureAwait(false);
        }
        catch (ProcessExecutionException)
        {
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
            catch (ProcessExecutionException)
            {
                return await OpenReadUsingCopyAsync(id, path, cancellationToken).ConfigureAwait(false);
            }
        }
    }

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
        var tempFile = Path.Combine(Path.GetTempPath(), "MezTC_" + Guid.NewGuid().ToString("N"));
        try
        {
            await Cli.RunBufferedAsync(BuildCopyFromContainerArguments(id, path, tempFile), cancellationToken).ConfigureAwait(false);
            return new TemporaryFileStream(tempFile);
        }
        catch
        {
            File.Delete(tempFile);
            throw;
        }
    }

    internal override async Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "MezTC_" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var fileStream = File.Create(tempFile))
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

    protected static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
            return null;

        // Runtimes report the zero date for events that never happened (for example FinishedAt on a running container).
        return result.UtcDateTime.Year <= 1 ? null : result;
    }
}
