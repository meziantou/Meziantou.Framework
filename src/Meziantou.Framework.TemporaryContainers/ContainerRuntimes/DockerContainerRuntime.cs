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
        // The operating system of the daemon decides how a port is published, and 'version' fails when the daemon does
        // not answer, so one command answers both questions.
        Flavor.Docker => ["version", "--format", "{{.Server.Os}}"],

        // wslc reports its version through '--version', which the CLI answers on its own, so the probe lists the containers instead.
        Flavor.Wslc => ["list", "-q"],
        _ => ["version"],
    };

    internal override bool LogsIncludeTimestamps => true;

    internal override bool SupportsPause => true;

    internal override bool SupportsRestart => _flavor is not Flavor.Wslc;

    // wslc is not known to read an environment file, so it keeps the variables on its command line.
    internal override bool SupportsEnvironmentFile => _flavor is not Flavor.Wslc;

    // wslc is not known to label the images it builds, so they cannot be found again.
    internal override bool SupportsImageCleanup => _flavor is not Flavor.Wslc;

    /// <summary>Windows containers cannot publish a port on a specific host address, and wslc publishes through the loopback forwarding of WSL.</summary>
    private bool IsHostIpSupported => _flavor switch
    {
        Flavor.Wslc => false,
        Flavor.Docker => !string.Equals(ProbeOutput?.Trim(), "windows", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    internal override async Task<string> PrepareImageAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        var logger = definition.Logging.Logger;
        switch (definition.Image)
        {
            case RegistryImage registry when _flavor is Flavor.Wslc:
                if (definition.PullPolicy is PullPolicy.Never)
                    throw new NotSupportedException($"The 'wslc' runtime does not support the '{nameof(PullPolicy.Never)}' pull policy.");

                // wslc pulls a missing image when it creates the container.
                if (definition.PullPolicy is PullPolicy.Always)
                    await PullImageAsync(["pull", registry.Name], registry.Name, logger, cancellationToken).ConfigureAwait(false);

                return registry.Name;

            case RegistryImage registry:
                // The image is pulled here, where a transient registry failure is retried, rather than by the create
                // command, which then never pulls.
                await EnsureRegistryImageAsync(["image", "inspect", registry.Name], ["pull", registry.Name], registry.Name, definition.PullPolicy, logger, cancellationToken).ConfigureAwait(false);
                return registry.Name;

            case DockerfileImage dockerfile:
                var tag = ResourceNaming.BuiltImagePrefix + Guid.NewGuid().ToString("N") + ":latest";
                var args = new List<string> { "build", "-t", tag, "-f", dockerfile.DockerfilePath };
                if (SupportsImageCleanup)
                {
                    // The labels are what the cleanup and the reaper find a built image by.
                    foreach (var (name, value) in ResourceLabels.BuildForImage(definition))
                    {
                        args.Add("--label");
                        args.Add($"{name}={value}");
                    }
                }

                args.Add(dockerfile.ContextDirectory);
                await Cli.RunBufferedAsync(args, cancellationToken).ConfigureAwait(false);
                return tag;

            case ArchiveImage archive:
                var loadResult = await Cli.RunBufferedAsync(["load", "-i", archive.ArchivePath], cancellationToken).ConfigureAwait(false);
                return ContainerImageOutputParser.TryParseLoadedImage(loadResult.StandardOutput)
                    ?? throw new ContainerRuntimeException("Unable to determine the image reference from the load output: " + loadResult.StandardOutput, this, statusCode: null);

            case ExistingImage existing:
                return existing.ImageId;

            default:
                throw new NotSupportedException($"Image source '{definition.Image.GetType()}' is not supported.");
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

        var lookup = await Cli.RunBufferedAsync(lookupArgs, cancellationToken).ConfigureAwait(false);
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

    internal override IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef, EnvironmentFile? environmentFile = null)
    {
        // The image is already there: PrepareImageAsync pulled it when it had to.
        var pullPolicyValue = _flavor is Flavor.Wslc ? null : definition.Image switch
        {
            RegistryImage or ExistingImage => "never",
            _ => null,
        };

        return DockerCreateArgumentBuilder.Build(definition, imageRef, pullPolicyValue, quotedMountFieldsSupported: _flavor is Flavor.Docker, IsHostIpSupported, environmentFile);
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

    internal override IReadOnlyList<string> BuildLogsArguments(string id, bool follow = true)
        => follow ? ["logs", "-f", "--timestamps", id] : ["logs", "--timestamps", id];

    internal override IReadOnlyList<string> BuildDeleteImageArguments(string image) => ["image", "rm", image];

    internal override IReadOnlyList<string> BuildExecArguments(string id, ExecOptions options, EnvironmentFile? environmentFile = null)
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

        EnvironmentFile.AddArguments(args, options.Environment, environmentFile);

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

    internal override IReadOnlyList<string> BuildCreateVolumeArguments(VolumeDefinition definition, string name, string? instanceId = null)
    {
        EnsureVolumesSupported();

        var args = new List<string> { "volume", "create" };
        if (definition.Driver is { } driver)
        {
            args.Add("--driver");
            args.Add(driver);
        }

        var labels = ResourceLabels.Build(definition.Labels, definition.ReuseId, sessionOwned: true, definition.Identity);
        if (instanceId is not null)
            labels[ResourceLabels.Instance] = instanceId;

        foreach (var (labelName, labelValue) in labels)
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

    internal override IReadOnlyDictionary<string, string>? ParseVolumeLabels(string output)
        => DockerVolumeInspectResult.Parse(output) is [var volume, ..] ? volume.Labels : null;

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

    internal static IReadOnlyList<string> BuildListManagedImagesArguments()
        => ["images", "--no-trunc", "--quiet", "--filter", $"label={ResourceLabels.Managed}"];

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedContainersAsync(CancellationToken cancellationToken)
    {
        // A listing that fails is reported, rather than read as an empty daemon: the cleanup would claim there was
        // nothing to remove.
        var list = await Cli.RunBufferedAsync(BuildListManagedContainersArguments(), cancellationToken).ConfigureAwait(false);
        return await InspectResourcesAsync(list.StandardOutput, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedImagesAsync(CancellationToken cancellationToken)
    {
        var list = await Cli.RunBufferedAsync(BuildListManagedImagesArguments(), cancellationToken).ConfigureAwait(false);
        return await InspectResourcesAsync(list.StandardOutput, cancellationToken, "image").ConfigureAwait(false);
    }

    /// <summary>Reads the labels of the resources a listing printed the ids of.</summary>
    private async Task<IReadOnlyList<ManagedResource>> InspectResourcesAsync(string listing, CancellationToken cancellationToken, string? objectType = null)
    {
        var ids = new List<string>();
        foreach (var line in listing.Split('\n'))
        {
            // Image ids are printed with their 'sha256:' prefix.
            var trimmed = line.Trim();
            if (IsContainerId(trimmed.StartsWith("sha256:", StringComparison.Ordinal) ? trimmed["sha256:".Length..] : trimmed))
                ids.Add(trimmed);
        }

        if (ids.Count == 0)
            return [];

        var resources = new List<ManagedResource>(ids.Count);
        foreach (var batch in GetInspectBatches(ids))
        {
            // A resource removed in the meantime makes the command fail while the others are still reported, so the
            // exit code is ignored and whatever was printed is parsed.
            var inspectArgs = objectType is null ? new List<string> { "inspect" } : new List<string> { objectType, "inspect" };
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
        var list = await Cli.RunBufferedAsync(BuildListManagedVolumesArguments(), cancellationToken).ConfigureAwait(false);
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

    /// <summary>The socket of the daemon, as its containers see it.</summary>
    /// <remarks>podman reports it: the socket of its service, rootless included, and the one inside the machine on macOS and Windows. The docker CLI reports the socket it connects to, which is the daemon's own on Linux, and a forwarding on the host on macOS and Windows, where the daemon runs in a virtual machine and has its socket at the default path.</remarks>
    internal override async Task<string> GetReaperSocketPathAsync(CancellationToken cancellationToken)
    {
        if (_flavor is Flavor.Podman)
        {
            // The watchdog drives the socket of the podman service, which is not running on every machine. podman reports
            // the socket it is configured with rather than one that exists, so on Linux, where that socket is a file of
            // this machine, it is the file that decides.
            var info = await Cli.RunBufferedAsync(["info", "--format", "{{.Host.RemoteSocket.Path}}"], cancellationToken).ConfigureAwait(false);
            var socketPath = StripUnixScheme(info.StandardOutput.Trim());
            if (string.IsNullOrEmpty(socketPath) || (OperatingSystem.IsLinux() && !File.Exists(socketPath)))
                throw new NotSupportedException($"The socket of the podman service ('{socketPath}') is not available. Start it with 'podman system service', or set ContainerReaperOptions.SocketPath.");

            return socketPath;
        }

        if (!OperatingSystem.IsLinux())
            return DefaultReaperSocketPath;

        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (string.IsNullOrWhiteSpace(dockerHost))
        {
            var context = await Cli.RunBufferedAsync(["context", "inspect", "--format", "{{.Endpoints.docker.Host}}"], cancellationToken, allowNonZero: true).ConfigureAwait(false);
            dockerHost = context.ExitCode == 0 ? context.StandardOutput.Trim() : null;
        }

        return dockerHost is not null && dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase)
            ? StripUnixScheme(dockerHost)
            : DefaultReaperSocketPath;
    }

    private static string StripUnixScheme(string value)
        => value.StartsWith("unix://", StringComparison.OrdinalIgnoreCase) ? Uri.UnescapeDataString(value["unix://".Length..]) : value;

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
                throw new ContainerRuntimeException("Unable to write file to the container. " + result.StandardError, this, statusCode: null);

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
            // wslc has no copy command, so the adapter streams the file itself. A directory cannot be streamed, and
            // reading one reports an error of 'cat' that says nothing about what the runtime cannot do.
            var isDirectory = await ExecAsync(id, BuildShellCommand("[ -d " + QuoteShellArgument(source) + " ]"), cancellationToken).ConfigureAwait(false);
            if (isDirectory.ExitCode == 0)
                throw new NotSupportedException($"The 'wslc' runtime cannot copy the directory '{source}': it has no copy command, and only a single file can be streamed out of a container.");

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

    private static ExecOptions BuildShellCommand(string command)
    {
        var options = new ExecOptions();
        options.Command.Add("sh");
        options.Command.Add("-c");
        options.Command.Add(command);
        return options;
    }

    private static string QuoteShellArgument(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
