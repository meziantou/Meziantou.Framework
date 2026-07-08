using System.Globalization;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Encapsulates the CLI dialect of a specific container runtime (command construction and inspect parsing).</summary>
internal abstract class ContainerRuntimeAdapter
{
    protected ContainerRuntimeAdapter(ContainerRuntime runtime, string executable)
    {
        Runtime = runtime;
        Executable = executable;
    }

    public ContainerRuntime Runtime { get; }

    public string Executable { get; }

    /// <summary>Whether <see cref="BuildLogsArguments"/> requests timestamps that must be stripped from each line.</summary>
    public abstract bool LogsIncludeTimestamps { get; }

    /// <summary>Whether the runtime supports pause/unpause.</summary>
    public abstract bool SupportsPause { get; }

    /// <summary>Whether the runtime has a native restart command (otherwise restart is emulated as stop + start).</summary>
    public abstract bool SupportsRestart { get; }

    public static ContainerRuntimeAdapter Create(ContainerRuntime runtime, string executable)
    {
        return runtime switch
        {
            ContainerRuntime.AppleContainer => new AppleContainerRuntimeAdapter(executable),
            _ => new DockerRuntimeAdapter(runtime, executable),
        };
    }

    public abstract Task<string> PrepareImageAsync(ContainerCli cli, ImageSource source, PullPolicy pullPolicy, CancellationToken cancellationToken);

    public abstract Task<string?> FindReusableContainerAsync(ContainerCli cli, string reuseId, CancellationToken cancellationToken);

    public abstract IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef);

    public abstract IReadOnlyList<string> BuildStartArguments(string id);

    public abstract IReadOnlyList<string> BuildStopArguments(string id);

    public abstract IReadOnlyList<string> BuildRestartArguments(string id);

    public abstract IReadOnlyList<string> BuildPauseArguments(string id);

    public abstract IReadOnlyList<string> BuildUnpauseArguments(string id);

    public abstract IReadOnlyList<string> BuildKillArguments(string id);

    public abstract IReadOnlyList<string> BuildRemoveArguments(string id);

    public abstract IReadOnlyList<string> BuildExistsArguments(string id);

    public abstract IReadOnlyList<string> BuildInspectArguments(string id);

    public abstract IReadOnlyList<string> BuildLogsArguments(string id);

    public abstract IReadOnlyList<string> BuildExecArguments(string id, ExecOptions options);

    public abstract IReadOnlyList<string> BuildCopyToContainerArguments(string id, string source, string destination);

    public abstract IReadOnlyList<string> BuildCopyFromContainerArguments(string id, string source, string destination);

    public abstract ContainerInfo ParseInspect(string output);

    public abstract IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition);

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
