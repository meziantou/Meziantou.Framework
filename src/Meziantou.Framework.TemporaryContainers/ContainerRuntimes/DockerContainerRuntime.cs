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

    public DockerContainerRuntime(string name, Flavor flavor, string? executablePath = null)
        : base(name, executablePath)
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

    internal override IReadOnlyList<string> BuildProbeArguments() => _flavor switch
    {
        // wslc reports its version through '--version', which the CLI answers on its own, so the probe lists the containers instead.
        Flavor.Wslc => ["list", "-q"],
        _ => ["version"],
    };

    internal override bool LogsIncludeTimestamps => true;

    internal override bool SupportsPause => true;

    internal override bool SupportsRestart => _flavor is not Flavor.Wslc;

    internal override async Task<string> PrepareImageAsync(ImageSource source, PullPolicy pullPolicy, ILogger? logger, CancellationToken cancellationToken)
    {
        switch (source)
        {
            case RegistryImage registry:
                // PullPolicy.IfMissing is not handled here: it is passed to the create command as '--pull=missing',
                // which is where a registry failure surfaces for that policy.
                if (pullPolicy is PullPolicy.Always)
                    await PullImageAsync(["pull", registry.Name], registry.Name, logger, cancellationToken).ConfigureAwait(false);
                return registry.Name;

            case DockerfileImage dockerfile:
                var tag = "meziantou-tc/" + Guid.NewGuid().ToString("N") + ":latest";
                await Cli.RunBufferedAsync(["build", "-t", tag, "-f", dockerfile.DockerfilePath, dockerfile.ContextDirectory], cancellationToken).ConfigureAwait(false);
                return tag;

            case ArchiveImage archive:
                var loadResult = await Cli.RunBufferedAsync(["load", "-i", archive.ArchivePath], cancellationToken).ConfigureAwait(false);
                return ContainerImageOutputParser.TryParseLoadedImage(loadResult.StandardOutput)
                    ?? throw new InvalidOperationException("Unable to determine the image reference from the load output: " + loadResult.StandardOutput);

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
            lookupArgs = ["list", "-a", "-q", "--filter", $"label={ResourceLabels.ReuseId}={reuseId}"];
        }
        else
        {
            lookupArgs = ["ps", "-a", "--no-trunc", "--filter", $"label={ResourceLabels.ReuseId}={reuseId}", "--format", "{{.ID}}"];
        }

        var lookup = await Cli.RunBufferedAsync(lookupArgs, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        foreach (var line in lookup.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!IsContainerId(trimmed))
                continue;

            return _flavor is Flavor.Wslc
                ? await ExpandContainerIdAsync(trimmed, cancellationToken).ConfigureAwait(false)
                : trimmed;
        }

        return null;
    }

    /// <summary>Expands a truncated id to the one the runtime reports for the container. 'wslc list -q' only prints the short id, where docker has '--no-trunc', and an adopted container has to report the same id as the run that created it.</summary>
    private async Task<string> ExpandContainerIdAsync(string id, CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(BuildInspectArguments(id), cancellationToken, allowNonZero: true).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return id;

        var info = ParseInspect(result.StandardOutput);
        return string.IsNullOrEmpty(info.Id) ? id : info.Id;
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

        return DockerCreateArgumentBuilder.Build(definition, imageRef, pullPolicyValue, quotedMountFieldsSupported: _flavor is Flavor.Docker);
    }

    internal override IReadOnlyList<string> BuildStartArguments(string id) => ["start", id];

    internal override IReadOnlyList<string> BuildStopArguments(string id) => ["stop", id];

    internal override IReadOnlyList<string> BuildRestartArguments(string id) => ["restart", id];

    internal override IReadOnlyList<string> BuildPauseArguments(string id) => ["pause", id];

    internal override IReadOnlyList<string> BuildUnpauseArguments(string id) => ["unpause", id];

    internal override IReadOnlyList<string> BuildKillArguments(string id) => ["kill", id];

    // '-v' removes the anonymous volumes the image declared, which would otherwise pile up after every run. Named
    // volumes are never touched by it. wslc has no such flag.
    internal override IReadOnlyList<string> BuildRemoveArguments(string id)
        => _flavor is Flavor.Wslc ? ["rm", "-f", id] : ["rm", "-f", "-v", id];

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

    internal override IReadOnlyList<string> BuildCreateVolumeArguments(VolumeDefinition definition, string name)
    {
        EnsureVolumesSupported();

        var args = new List<string> { "volume", "create" };
        if (definition.Driver is { } driver)
        {
            args.Add("--driver");
            args.Add(driver);
        }

        foreach (var (labelName, labelValue) in ResourceLabels.Build(definition.Labels, definition.ReuseId, sessionOwned: true, definition.Identity))
        {
            args.Add("--label");
            args.Add($"{labelName}={labelValue}");
        }

        foreach (var (optionName, optionValue) in definition.DriverOptions)
        {
            args.Add("--opt");
            args.Add($"{optionName}={optionValue}");
        }

        args.Add(name);
        return args;
    }

    // '--force' is deliberately not used: on docker it only hides the not-found error, which the caller already
    // tolerates, while on podman it also removes the containers using the volume.
    internal override IReadOnlyList<string> BuildDeleteVolumeArguments(string name)
    {
        EnsureVolumesSupported();
        return ["volume", "rm", name];
    }

    internal override IReadOnlyList<string> BuildVolumeExistsArguments(string name)
    {
        EnsureVolumesSupported();
        return ["volume", "inspect", name];
    }

    internal override bool SupportsVolumes => _flavor is not Flavor.Wslc;

    internal override bool SupportsReaper => _flavor is not Flavor.Wslc;

    internal IReadOnlyList<string> BuildListManagedContainersArguments()
        => _flavor is Flavor.Wslc
            ? ["list", "-a", "-q", "--filter", $"label={ResourceLabels.Managed}"]
            : ["ps", "-a", "--no-trunc", "--filter", $"label={ResourceLabels.Managed}", "--format", "{{.ID}}"];

    internal IReadOnlyList<string> BuildListManagedVolumesArguments()
    {
        EnsureVolumesSupported();
        return ["volume", "ls", "--filter", $"label={ResourceLabels.Managed}", "--format", "{{.Name}}"];
    }

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedContainersAsync(CancellationToken cancellationToken)
    {
        var list = await Cli.RunBufferedAsync(BuildListManagedContainersArguments(), cancellationToken, allowNonZero: true).ConfigureAwait(false);
        var ids = new List<string>();
        foreach (var line in list.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (IsContainerId(trimmed))
                ids.Add(trimmed);
        }

        if (ids.Count == 0)
            return [];

        var resources = new List<ManagedResource>(ids.Count);
        foreach (var batch in GetInspectBatches(ids))
        {
            // A container removed in the meantime makes the command fail while the others are still reported, so the
            // exit code is ignored and whatever was printed is parsed.
            var inspectArgs = new List<string> { "inspect" };
            inspectArgs.AddRange(batch);
            var inspect = await Cli.RunBufferedAsync(inspectArgs, cancellationToken, allowNonZero: true).ConfigureAwait(false);
            foreach (var info in DockerContainerInfoParser.ParseInspectOutputs(inspect.StandardOutput))
            {
                if (!string.IsNullOrEmpty(info.Id))
                    resources.Add(new ManagedResource(info.Id, info.Labels));
            }
        }

        return resources;
    }

    /// <summary>Groups the resources to inspect. docker and podman inspect a whole batch at once; wslc only documents a single argument, so it is asked one at a time.</summary>
    private IEnumerable<IReadOnlyList<string>> GetInspectBatches(List<string> names)
    {
        if (_flavor is not Flavor.Wslc)
        {
            yield return names;
            yield break;
        }

        foreach (var name in names)
            yield return [name];
    }

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedVolumesAsync(CancellationToken cancellationToken)
    {
        var list = await Cli.RunBufferedAsync(BuildListManagedVolumesArguments(), cancellationToken, allowNonZero: true).ConfigureAwait(false);
        var names = new List<string>();
        foreach (var line in list.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                names.Add(trimmed);
        }

        if (names.Count == 0)
            return [];

        var inspectArgs = new List<string> { "volume", "inspect" };
        inspectArgs.AddRange(names);
        var inspect = await Cli.RunBufferedAsync(inspectArgs, cancellationToken, allowNonZero: true).ConfigureAwait(false);
        return DockerVolumeInspectResult.Parse(inspect.StandardOutput);
    }



    private void EnsureVolumesSupported()
    {
        if (_flavor is Flavor.Wslc)
            throw new NotSupportedException("The 'wslc' CLI does not have volume commands.");
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
