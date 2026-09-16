using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>CLI dialect for Apple's <c>container</c> runtime (macOS). Best-effort: verified against the documented CLI, not executed in CI on non-macOS hosts.</summary>
internal sealed class AppleContainerRuntime : ExecutableContainerRuntime
{
    public AppleContainerRuntime(string name, string? executablePath = null)
        : base(name, executablePath)
    {
    }

    internal override string ExecutableName => "container";

    // 'container' is not a distinctive name: WSL containers installs a container.exe alias for wslc, which answers
    // the probe below and would then be driven with Apple's CLI dialect. Apple's runtime only exists on macOS, so
    // nothing else can legitimately claim the name there.
    public override Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
        => OperatingSystem.IsMacOS() ? base.IsSupportedAsync(cancellationToken) : Task.FromResult(false);

    // 'container --version' is answered by the CLI itself, so the probe has to go through the API server: listing the
    // containers is the cheapest command that does.
    internal override IReadOnlyList<string> BuildProbeArguments() => ["ls", "-q"];

    internal override bool SupportsImageCleanup => true;

    internal override void PrepareDefinitionForCreate(ContainerDefinition definition)
    {
        // Apple's container runtime does not support random-port assignment, so a free host port has to be picked
        // here. This only runs when a container is actually created: an adopted container keeps the host ports it was
        // created with. The ports are picked again for every creation, since the previous ones may have been taken.
        definition.AllocatedHostPorts.Clear();
        foreach (var port in definition.Ports)
        {
            if (port.HostPort is null)
                definition.AllocatedHostPorts[port.Port] = GetFreeTcpPort();
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Apple's runtime binds the host ports when it starts the container, so a port picked when the container was created can have been taken by then.</summary>
    internal override bool IsHostPortConflict(Exception exception)
        => exception is ContainerRuntimeException runtimeException &&
           (runtimeException.StandardError?.Contains("Address already in use", StringComparison.OrdinalIgnoreCase) is true ||
            runtimeException.Message.Contains("Address already in use", StringComparison.OrdinalIgnoreCase));

    internal override async Task<string> PrepareImageAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        switch (definition.Image)
        {
            case RegistryImage registry:
                await EnsureRegistryImageAsync(["image", "inspect", registry.Name], ["image", "pull", registry.Name], registry.Name, definition.PullPolicy, definition.Logging.Logger, cancellationToken).ConfigureAwait(false);
                return registry.Name;

            case DockerfileImage dockerfile:
                var tag = ResourceNaming.BuiltImagePrefix + Guid.NewGuid().ToString("N") + ":latest";
                var args = new List<string> { "build", "-t", tag, "-f", dockerfile.DockerfilePath };

                // The labels are what the cleanup finds a built image by.
                foreach (var (name, value) in ResourceLabels.BuildForImage(definition))
                {
                    args.Add("--label");
                    args.Add($"{name}={value}");
                }

                args.Add(dockerfile.ContextDirectory);
                await Cli.RunBufferedAsync(args, cancellationToken).ConfigureAwait(false);
                return tag;

            case ArchiveImage archive:
                var loadResult = await Cli.RunBufferedAsync(["image", "load", "-i", archive.ArchivePath], cancellationToken).ConfigureAwait(false);
                // Apple's load output is not documented, so an unrecognised shape falls back to the raw output
                // rather than failing outright.
                return ContainerImageOutputParser.TryParseLoadedImage(loadResult.StandardOutput)
                    ?? loadResult.StandardOutput.Trim();

            case ExistingImage existing:
                return existing.ImageId;

            default:
                throw new NotSupportedException($"Image source '{definition.Image.GetType()}' is not supported.");
        }
    }

    /// <summary>Finds the container created with a reuse identifier by its label, like the other runtimes do. Looking it up by name would adopt any container that happens to have that name.</summary>
    internal override async Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(["ls", "-a", "--format", "json"], cancellationToken).ConfigureAwait(false);
        foreach (var resource in ParseResources(result.StandardOutput, managedOnly: true))
        {
            if (resource.Labels.TryGetValue(ResourceLabels.ReuseId, out var value) && string.Equals(value, reuseId, StringComparison.Ordinal))
                return resource.Id;
        }

        return null;
    }

    internal override IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef, EnvironmentFile? environmentFile = null)
    {
        if (definition.Network.Alias is not null)
            throw new NotSupportedException("Apple's container runtime does not support network aliases.");

        if (definition.Hostname is not null)
            throw new NotSupportedException("Apple's container runtime does not support setting the hostname: the container is named after its id.");

        var args = new List<string> { "create" };

        var name = definition.Name ?? (definition.ReuseId is { } reuseId ? ResourceNaming.GetReuseName(reuseId) : null);
        AddOption(args, "--name", name);
        AddOption(args, "--network", definition.Network.Network);

        if (definition.Resources.ReadOnlyRootFilesystem)
            args.Add("--read-only");
        AddOption(args, "--user", definition.User);
        AddOption(args, "--workdir", definition.WorkingDirectory);

        if (definition.Resources.MemoryLimit is { } memory)
            AddOption(args, "--memory", memory.ToString(CultureInfo.InvariantCulture));

        if (definition.Resources.CpuLimit is { } cpu)
            AddOption(args, "--cpus", cpu.ToString(CultureInfo.InvariantCulture));

        foreach (var (labelName, labelValue) in ResourceLabels.Build(definition))
        {
            args.Add("--label");
            args.Add($"{labelName}={labelValue}");
        }

        EnvironmentFile.AddArguments(args, definition.Environment, environmentFile);

        foreach (var port in definition.Ports)
        {
            if (port.HostIp.Contains(':', StringComparison.Ordinal))
                throw new NotSupportedException($"Apple's container runtime cannot publish a port on the IPv6 address '{port.HostIp}'.");

            args.Add("--publish");
            args.Add(string.Create(CultureInfo.InvariantCulture, $"{port.HostIp}:{GetHostPort(port, definition)}:{port.Port}"));
        }

        foreach (var mount in definition.Mounts)
            AppendMount(args, mount);

        var entrypoint = new List<string>(definition.Entrypoint);
        if (entrypoint.Count > 0)
            AddOption(args, "--entrypoint", entrypoint[0]);

        args.Add(imageRef);

        for (var i = 1; i < entrypoint.Count; i++)
            args.Add(entrypoint[i]);

        foreach (var token in definition.Command)
            args.Add(token);

        return args;
    }

    private static int GetHostPort(ContainerPort port, ContainerDefinition definition)
        => port.HostPort ?? (definition.AllocatedHostPorts.TryGetValue(port.Port, out var allocated) ? allocated : port.Port);

    internal override IReadOnlyList<string> BuildCreateVolumeArguments(VolumeDefinition definition, string name, string? instanceId = null)
    {
        if (definition.Driver is not null)
            throw new NotSupportedException("Apple's container runtime does not support volume drivers.");

        var labels = ResourceLabels.Build(definition.Labels, definition.ReuseId, sessionOwned: true, definition.Identity);
        if (instanceId is not null)
            labels[ResourceLabels.Instance] = instanceId;

        var args = new List<string> { "volume", "create" };
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

    // Apple's CLI has no label filter, so everything is listed and filtered here. Both listings report the same shape
    // as 'inspect', labels included, which spares an inspect per resource. A listing that fails is reported, rather
    // than read as an empty daemon.
    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedContainersAsync(CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(["ls", "-a", "--format", "json"], cancellationToken).ConfigureAwait(false);
        return ParseManagedResources(result.StandardOutput);
    }

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedVolumesAsync(CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(["volume", "ls", "--format", "json"], cancellationToken).ConfigureAwait(false);
        return ParseManagedResources(result.StandardOutput);
    }

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedImagesAsync(CancellationToken cancellationToken)
    {
        var result = await Cli.RunBufferedAsync(["image", "list", "--format", "json"], cancellationToken).ConfigureAwait(false);
        return ParseManagedImages(result.StandardOutput);
    }

    internal static IReadOnlyList<ManagedResource> ParseManagedResources(string output) => ParseResources(output, managedOnly: true);

    private static List<ManagedResource> ParseResources(string output, bool managedOnly)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        AppleInspectResult[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(output, AppleInspectJsonContext.Default.AppleInspectResultArray);
        }
        catch (JsonException)
        {
            return [];
        }

        if (parsed is null)
            return [];

        var resources = new List<ManagedResource>();
        foreach (var item in parsed)
        {
            var labels = item.Configuration?.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
            if (managedOnly && !labels.ContainsKey(ResourceLabels.Managed))
                continue;

            var id = item.Id ?? item.Configuration?.Id;
            if (!string.IsNullOrEmpty(id))
                resources.Add(new ManagedResource(id, labels));
        }

        return resources;
    }

    /// <summary>Reads the images this library built out of an image listing. An image is named by its reference, which is what the runtime deletes it by.</summary>
    internal static IReadOnlyList<ManagedResource> ParseManagedImages(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        AppleImageDto[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(output, AppleInspectJsonContext.Default.AppleImageDtoArray);
        }
        catch (JsonException)
        {
            return [];
        }

        var resources = new List<ManagedResource>();
        foreach (var image in parsed ?? [])
        {
            if (image.Configuration?.Name is not { Length: > 0 } name)
                continue;

            var labels = image.Variants?.Select(static variant => variant.Config?.Config?.Labels).FirstOrDefault(static labels => labels?.ContainsKey(ResourceLabels.Managed) is true);
            if (labels is not null)
                resources.Add(new ManagedResource(name, labels));
        }

        return resources;
    }

    internal override IReadOnlyDictionary<string, string>? ParseVolumeLabels(string output)
        => ParseResources(output, managedOnly: false) is [var volume, ..] ? volume.Labels : null;

    internal override IReadOnlyList<string> BuildDeleteVolumeArguments(string name) => ["volume", "delete", name];

    internal override IReadOnlyList<string> BuildVolumeExistsArguments(string name) => ["volume", "inspect", name];

    internal override IReadOnlyList<string> BuildDeleteImageArguments(string image) => ["image", "delete", image];

    internal override IReadOnlyList<string> BuildStartArguments(string id) => ["start", id];

    internal override IReadOnlyList<string> BuildStopArguments(string id) => ["stop", id];

    internal override IReadOnlyList<string> BuildKillArguments(string id) => ["kill", id];

    internal override IReadOnlyList<string> BuildRemoveArguments(string id) => ["delete", "--force", id];

    internal override IReadOnlyList<string> BuildExistsArguments(string id) => ["inspect", id];

    internal override IReadOnlyList<string> BuildInspectArguments(string id) => ["inspect", id];

    internal override IReadOnlyList<string> BuildLogsArguments(string id, bool follow = true)
        => follow ? ["logs", "--follow", id] : ["logs", id];

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
        => ["copy", source, $"{id}:{destination}"];

    internal override IReadOnlyList<string> BuildCopyFromContainerArguments(string id, string source, string destination)
        => ["copy", $"{id}:{source}", destination];

    internal override ContainerInfo ParseInspect(string output)
    {
        var parsed = JsonSerializer.Deserialize(output, AppleInspectJsonContext.Default.AppleInspectResultArray);
        if (parsed is null || parsed.Length == 0)
            throw new InvalidOperationException("Unable to inspect the container: the runtime returned no data.");

        var result = parsed[0];
        var id = result.Id ?? result.Configuration?.Id ?? "";
        var address = GetAddress(result);
        var slash = address?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        var status = GetStatus(result.Status);

        return new ContainerInfo
        {
            Id = id,
            Name = id,
            Image = GetImage(result.Configuration?.Image),
            State = ParseState(status),
            Status = status,
            IPAddress = slash >= 0 ? address![..slash] : address,
            Ports = GetPorts(result.Configuration?.PublishedPorts),
            Labels = result.Configuration?.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Environment = DockerContainerInfoParser.ParseEnvironment(result.Configuration?.InitProcess?.Environment),
        };
    }

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(info);

        // The runtime reports the bindings it actually created, which is the only source that is correct for a
        // container adopted through ReuseId: its host ports were chosen by whichever run created it.
        if (info.Ports.Count > 0)
            return info.Ports;

        // Older CLI versions do not report 'publishedPorts', so fall back to what the container was created with.
        var map = new Dictionary<int, int>();
        foreach (var port in definition.Ports)
            map[port.Port] = GetHostPort(port, definition);

        return map;
    }

    private static Dictionary<int, int> GetPorts(List<ApplePublishedPortDto>? publishedPorts)
    {
        var ports = new Dictionary<int, int>();
        if (publishedPorts is null)
            return ports;

        foreach (var port in publishedPorts)
        {
            if (port.Proto is null or "tcp" && port.HostPort > 0)
                ports[port.ContainerPort] = port.HostPort;
        }

        return ports;
    }

    private static ContainerState ParseState(string? status)
    {
        return status switch
        {
            "created" => ContainerState.Created,
            "running" => ContainerState.Running,
            "stopped" or "exited" => ContainerState.Exited,
            _ => ContainerState.Unknown,
        };
    }

    private static string? GetStatus(JsonElement status)
    {
        if (status.ValueKind is JsonValueKind.String)
            return status.GetString();

        if (status.ValueKind is JsonValueKind.Object &&
            status.TryGetProperty("state", out var stateElement) &&
            stateElement.ValueKind is JsonValueKind.String)
        {
            return stateElement.GetString();
        }

        return null;
    }

    private static string? GetAddress(AppleInspectResult result)
    {
        var status = result.Status;
        if (status.ValueKind is JsonValueKind.Object &&
            status.TryGetProperty("networks", out var networksElement) &&
            networksElement.ValueKind is JsonValueKind.Array)
        {
            foreach (var network in networksElement.EnumerateArray())
            {
                if (network.ValueKind is not JsonValueKind.Object)
                    continue;

                if (network.TryGetProperty("ipv4Address", out var ipv4Element) && ipv4Element.ValueKind is JsonValueKind.String)
                    return ipv4Element.GetString();

                if (network.TryGetProperty("address", out var addressElement) && addressElement.ValueKind is JsonValueKind.String)
                    return addressElement.GetString();
            }
        }

        if (result.Networks is { Count: > 0 })
            return result.Networks[0].Ipv4Address ?? result.Networks[0].Address;

        return null;
    }

    private static string? GetImage(JsonElement? image)
    {
        if (image is null)
            return null;

        var value = image.Value;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("reference", out var referenceElement) && referenceElement.ValueKind is JsonValueKind.String => referenceElement.GetString(),
            _ => null,
        };
    }

    private static void AddOption(List<string> args, string flag, string? value)
    {
        if (value is not null)
        {
            args.Add(flag);
            args.Add(value);
        }
    }

    private static void AppendMount(List<string> args, IMount mount)
    {
        switch (mount)
        {
            case BindMount bind:
                AddMountDescriptor(args, "bind", bind.Source, bind.Target, bind.ReadOnly);
                break;

            case VolumeMount volume:
                AddMountDescriptor(args, "volume", volume.Name, volume.Target, volume.ReadOnly);
                break;

            case OwnedVolumeMount owned:
                AddMountDescriptor(args, "volume", owned.Volume.Name, owned.Target, owned.ReadOnly);
                break;

            case TmpfsMount tmpfs:
                args.Add("--tmpfs");
                args.Add(tmpfs.Target);
                break;

            default:
                throw new NotSupportedException($"Mount type '{mount.GetType()}' is not supported.");
        }
    }

    private static void AddMountDescriptor(List<string> args, string type, string source, string target, bool readOnly)
    {
        // Apple's parser splits the descriptor on ',' without honouring quotes, so a value containing one cannot be
        // expressed at all.
        EnsureDescriptorValue(source);
        EnsureDescriptorValue(target);

        args.Add("--mount");
        args.Add(readOnly
            ? $"type={type},source={source},target={target},readonly"
            : $"type={type},source={source},target={target}");
    }

    private static void EnsureDescriptorValue(string value)
    {
        if (value.Contains(',', StringComparison.Ordinal))
            throw new NotSupportedException($"Apple's container runtime cannot mount '{value}' because the path contains a comma.");
    }
}
