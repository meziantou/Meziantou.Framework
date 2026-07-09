using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>CLI dialect for docker-compatible runtimes (docker, podman, and wslc).</summary>
internal sealed class DockerRuntimeAdapter(ContainerRuntime runtime, string executable) : ContainerRuntimeAdapter(runtime, executable)
{
    public override bool LogsIncludeTimestamps => true;

    public override bool SupportsPause => true;

    public override bool SupportsRestart => Runtime is not ContainerRuntime.Wslc;

    public override async Task<string> PrepareImageAsync(ContainerCli cli, ImageSource source, PullPolicy pullPolicy, CancellationToken cancellationToken)
    {
        switch (source)
        {
            case RegistryImage registry:
                if (pullPolicy is PullPolicy.Always)
                    await cli.RunBufferedAsync(["pull", registry.Name], cancellationToken).ConfigureAwait(false);
                return registry.Name;

            case DockerfileImage dockerfile:
                var tag = "meziantou-tc/" + Guid.NewGuid().ToString("N") + ":latest";
                await cli.RunBufferedAsync(["build", "-t", tag, "-f", dockerfile.DockerfilePath, dockerfile.ContextDirectory], cancellationToken).ConfigureAwait(false);
                return tag;

            case ArchiveImage archive:
                var loadResult = await cli.RunBufferedAsync(["load", "-i", archive.ArchivePath], cancellationToken).ConfigureAwait(false);
                return ParseLoadedImage(loadResult.StandardOutput);

            case ExistingImage existing:
                return existing.ImageId;

            default:
                throw new NotSupportedException($"Image source '{source.GetType()}' is not supported.");
        }
    }

    public override async Task<string?> FindReusableContainerAsync(ContainerCli cli, string reuseId, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> lookupArgs;
        if (Runtime is ContainerRuntime.Wslc)
        {
            lookupArgs = ["list", "-a", "-q", "--filter", $"label={DockerCreateArgumentBuilder.ReuseLabel}={reuseId}"];
        }
        else
        {
            lookupArgs = ["ps", "-a", "--no-trunc", "--filter", $"label={DockerCreateArgumentBuilder.ReuseLabel}={reuseId}", "--format", "{{.ID}}"];
        }

        var lookup = await cli.RunBufferedAsync(lookupArgs, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        foreach (var line in lookup.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (IsContainerId(trimmed))
                return trimmed;
        }

        return null;
    }

    public override IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef)
    {
        var pullPolicyValue = definition.Image switch
        {
            RegistryImage => definition.PullPolicy switch
            {
                PullPolicy.Always => "always",
                PullPolicy.Never => "never",
                _ => "missing",
            },
            ExistingImage => "never",
            _ => null,
        };

        if (Runtime is ContainerRuntime.Wslc)
            pullPolicyValue = null;

        return DockerCreateArgumentBuilder.Build(definition, imageRef, pullPolicyValue);
    }

    public override IReadOnlyList<string> BuildStartArguments(string id) => ["start", id];

    public override IReadOnlyList<string> BuildStopArguments(string id) => ["stop", id];

    public override IReadOnlyList<string> BuildRestartArguments(string id) => ["restart", id];

    public override IReadOnlyList<string> BuildPauseArguments(string id) => ["pause", id];

    public override IReadOnlyList<string> BuildUnpauseArguments(string id) => ["unpause", id];

    public override IReadOnlyList<string> BuildKillArguments(string id) => ["kill", id];

    public override IReadOnlyList<string> BuildRemoveArguments(string id) => ["rm", "-f", id];

    public override IReadOnlyList<string> BuildExistsArguments(string id)
        => Runtime is ContainerRuntime.Wslc
            ? ["inspect", id]
            : ["container", "inspect", "--format", "{{.Id}}", id];

    public override IReadOnlyList<string> BuildInspectArguments(string id) => ["inspect", id];

    public override IReadOnlyList<string> BuildLogsArguments(string id) => ["logs", "-f", "--timestamps", id];

    public override IReadOnlyList<string> BuildExecArguments(string id, ExecOptions options)
    {
        var args = new List<string> { "exec" };
        if (options.StandardInput is not null)
            args.Add("-i");

        if (options.WorkingDirectory is not null)
        {
            args.Add("--workdir");
            args.Add(options.WorkingDirectory);
        }

        if (options.User is not null)
        {
            args.Add("--user");
            args.Add(options.User);
        }

        foreach (var (name, value) in options.Environment)
        {
            args.Add("--env");
            args.Add($"{name}={value}");
        }

        args.Add(id);
        args.AddRange(options.Command);
        return args;
    }

    public override IReadOnlyList<string> BuildCopyToContainerArguments(string id, string source, string destination)
        => ["cp", source, $"{id}:{destination}"];

    public override IReadOnlyList<string> BuildCopyFromContainerArguments(string id, string source, string destination)
        => ["cp", $"{id}:{source}", destination];

    public override ContainerInfo ParseInspect(string output)
    {
        var parsed = JsonSerializer.Deserialize(output, DockerInspectJsonContext.Default.DockerInspectResultArray);
        if (parsed is null || parsed.Length == 0)
            throw new InvalidOperationException("Unable to inspect the container: the runtime returned no data.");

        var result = parsed[0];
        var ports = new Dictionary<int, int>();
        var portBindings = result.NetworkSettings?.Ports ?? result.Ports;
        if (portBindings is not null)
        {
            foreach (var (key, bindings) in portBindings)
            {
                if (bindings is not { Count: > 0 })
                    continue;

                var slash = key.IndexOf('/', StringComparison.Ordinal);
                var portText = slash >= 0 ? key[..slash] : key;
                if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var containerPort))
                    continue;

                if (int.TryParse(bindings[0].HostPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostPort))
                    ports[containerPort] = hostPort;
            }
        }

        return new ContainerInfo
        {
            Id = result.Id ?? "",
            Name = (result.Name ?? "").TrimStart('/'),
            Image = result.Config?.Image ?? result.Image,
            State = ParseState(result.State?.Status),
            Status = result.State?.Status,
            StartedAt = ParseDate(result.State?.StartedAt),
            FinishedAt = ParseDate(result.State?.FinishedAt),
            ExitCode = result.State?.ExitCode,
            IPAddress = result.NetworkSettings?.IPAddress,
            Ports = ports,
            Labels = result.Config?.Labels ?? result.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    public override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition) => info.Ports;

    private static ContainerState ParseState(string? status)
    {
        return status switch
        {
            "created" => ContainerState.Created,
            "running" => ContainerState.Running,
            "paused" => ContainerState.Paused,
            "exited" or "dead" => ContainerState.Exited,
            "removing" => ContainerState.Removed,
            _ => ContainerState.Unknown,
        };
    }

    private static string ParseLoadedImage(string output)
    {
        // Output looks like: "Loaded image: repo:tag" or "Loaded image ID: sha256:...".
        const string Marker = "Loaded image";
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            var markerIndex = trimmed.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;

            var rest = trimmed[(markerIndex + Marker.Length)..];
            var colonIndex = rest.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex >= 0)
                return rest[(colonIndex + 1)..].Trim();
        }

        throw new InvalidOperationException("Unable to determine the image reference from the load output: " + output);
    }

    private static bool IsContainerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        return true;
    }
}
