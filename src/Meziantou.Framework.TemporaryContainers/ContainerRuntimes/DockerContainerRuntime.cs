using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Runtime implementation for docker-compatible CLIs (docker, podman, and wslc).</summary>
internal sealed class DockerContainerRuntime : ExecutableContainerRuntime
{
    internal enum Flavor
    {
        Docker,
        Podman,
        Wslc,
    }

    private readonly Flavor _flavor;

    public DockerContainerRuntime(string name, Flavor flavor)
        : base(name)
    {
        _flavor = flavor;
    }

    private DockerContainerRuntime(string name, Flavor flavor, string executable, ILogger? logger)
        : base(name, executable, logger)
    {
        _flavor = flavor;
    }

    internal override string ExecutableName => _flavor switch
    {
        Flavor.Docker => "docker",
        Flavor.Podman => "podman",
        Flavor.Wslc => "wslc",
        _ => throw new InvalidOperationException($"Unknown flavor: {_flavor}"),
    };

    internal override ContainerRuntime Bind(string executable, ILogger? logger)
    {
        return new DockerContainerRuntime(ToString(), _flavor, executable, logger);
    }

    internal override bool LogsIncludeTimestamps => true;

    internal override bool SupportsPause => true;

    internal override bool SupportsRestart => _flavor is not Flavor.Wslc;

    internal override async Task<string> PrepareImageAsync(ImageSource source, PullPolicy pullPolicy, CancellationToken cancellationToken)
    {
        switch (source)
        {
            case RegistryImage registry:
                if (pullPolicy is PullPolicy.Always)
                    await Cli.RunBufferedAsync(["pull", registry.Name], cancellationToken).ConfigureAwait(false);
                return registry.Name;

            case DockerfileImage dockerfile:
                var tag = "meziantou-tc/" + Guid.NewGuid().ToString("N") + ":latest";
                await Cli.RunBufferedAsync(["build", "-t", tag, "-f", dockerfile.DockerfilePath, dockerfile.ContextDirectory], cancellationToken).ConfigureAwait(false);
                return tag;

            case ArchiveImage archive:
                var loadResult = await Cli.RunBufferedAsync(["load", "-i", archive.ArchivePath], cancellationToken).ConfigureAwait(false);
                return ParseLoadedImage(loadResult.StandardOutput);

            case ExistingImage existing:
                return existing.ImageId;

            default:
                throw new NotSupportedException($"Image source '{source.GetType()}' is not supported.");
        }
    }

    internal override async Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> lookupArgs;
        if (_flavor is Flavor.Wslc)
        {
            lookupArgs = ["list", "-a", "-q", "--filter", $"label={DockerCreateArgumentBuilder.ReuseLabel}={reuseId}"];
        }
        else
        {
            lookupArgs = ["ps", "-a", "--no-trunc", "--filter", $"label={DockerCreateArgumentBuilder.ReuseLabel}={reuseId}", "--format", "{{.ID}}"];
        }

        var lookup = await Cli.RunBufferedAsync(lookupArgs, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        foreach (var line in lookup.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (IsContainerId(trimmed))
                return trimmed;
        }

        return null;
    }

    internal override IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef)
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

        if (_flavor is Flavor.Wslc)
            pullPolicyValue = null;

        return DockerCreateArgumentBuilder.Build(definition, imageRef, pullPolicyValue);
    }

    internal override IReadOnlyList<string> BuildStartArguments(string id) => ["start", id];

    internal override IReadOnlyList<string> BuildStopArguments(string id) => ["stop", id];

    internal override IReadOnlyList<string> BuildRestartArguments(string id) => ["restart", id];

    internal override IReadOnlyList<string> BuildPauseArguments(string id) => ["pause", id];

    internal override IReadOnlyList<string> BuildUnpauseArguments(string id) => ["unpause", id];

    internal override IReadOnlyList<string> BuildKillArguments(string id) => ["kill", id];

    internal override IReadOnlyList<string> BuildRemoveArguments(string id) => ["rm", "-f", id];

    internal override IReadOnlyList<string> BuildExistsArguments(string id)
        => _flavor is Flavor.Wslc
            ? ["inspect", id]
            : ["container", "inspect", "--format", "{{.Id}}", id];

    internal override IReadOnlyList<string> BuildInspectArguments(string id) => ["inspect", id];

    internal override IReadOnlyList<string> BuildLogsArguments(string id) => ["logs", "-f", "--timestamps", id];

    internal override IReadOnlyList<string> BuildExecArguments(string id, ExecOptions options)
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

    internal override IReadOnlyList<string> BuildCopyToContainerArguments(string id, string source, string destination)
        => ["cp", source, $"{id}:{destination}"];

    internal override IReadOnlyList<string> BuildCopyFromContainerArguments(string id, string source, string destination)
        => ["cp", $"{id}:{source}", destination];

    internal override ContainerInfo ParseInspect(string output)
    {
        return DockerContainerInfoParser.ParseInspectOutput(output);
    }

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition) => info.Ports;

    internal override async Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
    {
        if (_flavor is Flavor.Wslc)
        {
            var options = new ExecOptions
            {
                StandardInput = InputSource.FromStream(content),
            };
            options.Command.Add("sh");
            options.Command.Add("-c");
            options.Command.Add("cat > " + QuoteShellArgument(path));

            var result = await ExecAsync(id, options, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("Unable to write file to the container. " + result.StandardError);

            return;
        }

        await base.WriteFileAsync(id, path, content, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task CopyToContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        if (_flavor is Flavor.Wslc)
        {
            await using var stream = File.OpenRead(source);
            await WriteFileAsync(id, destination, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        await base.CopyToContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task CopyFromContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        if (_flavor is Flavor.Wslc)
        {
            await using var stream = await OpenReadAsync(id, source, cancellationToken).ConfigureAwait(false);
            await using var fileStream = File.Create(destination);
            await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        await base.CopyFromContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
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

    private static string QuoteShellArgument(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
