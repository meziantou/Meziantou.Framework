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

    internal override void PrepareDefinitionForCreate(ContainerDefinition definition)
    {
        // Apple's container runtime does not support random-port assignment, so a free host port has to be picked
        // here. This only runs when a container is actually created: an adopted container keeps the host ports it was
        // created with, and rewriting them would make GetMappedPort report ports nothing is listening on.
        var portsWithoutHostPort = new List<int>();
        foreach (var port in definition.Ports)
        {
            if (port.HostPort is null)
                portsWithoutHostPort.Add(port.Port);
        }

        foreach (var containerPort in portsWithoutHostPort)
        {
            definition.Ports.Remove(containerPort);
            definition.Ports.Add(GetFreeTcpPort(), containerPort);
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    internal override bool LogsIncludeTimestamps => false;

    internal override bool SupportsPause => false;

    internal override bool SupportsRestart => false;

    internal override async Task<string> PrepareImageAsync(ImageSource source, PullPolicy pullPolicy, CancellationToken cancellationToken)
    {
        switch (source)
        {
            case RegistryImage registry:
                if (pullPolicy is PullPolicy.Always)
                    await Cli.RunBufferedAsync(["image", "pull", registry.Name], cancellationToken).ConfigureAwait(false);
                return registry.Name;

            case DockerfileImage dockerfile:
                var tag = "meziantou-tc/" + Guid.NewGuid().ToString("N") + ":latest";
                await Cli.RunBufferedAsync(["build", "-t", tag, "-f", dockerfile.DockerfilePath, dockerfile.ContextDirectory], cancellationToken).ConfigureAwait(false);
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
                throw new NotSupportedException($"Image source '{source.GetType()}' is not supported.");
        }
    }

    internal override async Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken)
    {
        var name = ResourceNaming.GetReuseName(reuseId);
        var result = await Cli.RunBufferedAsync(["inspect", name], cancellationToken, allowNonZero: true).ConfigureAwait(false);
        return result.ExitCode == 0 ? name : null;
    }

    internal override IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef)
    {
        if (definition.Network.Alias is not null)
            throw new NotSupportedException("Apple's container runtime does not support network aliases.");

        var args = new List<string> { "create" };

        var name = definition.ReuseId is { } reuseId ? ResourceNaming.GetReuseName(reuseId) : definition.Name;
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

        foreach (var (labelName, labelValue) in definition.Labels)
        {
            args.Add("--label");
            args.Add($"{labelName}={labelValue}");
        }

        foreach (var (envName, envValue) in definition.Environment)
        {
            args.Add("--env");
            args.Add($"{envName}={envValue}");
        }

        foreach (var port in definition.Ports)
        {
            var hostPort = port.HostPort ?? port.Port;
            args.Add("--publish");
            args.Add(string.Create(CultureInfo.InvariantCulture, $"{hostPort}:{port.Port}"));
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

    internal override IReadOnlyList<string> BuildCreateVolumeArguments(VolumeDefinition definition, string name)
    {
        if (definition.Driver is not null)
            throw new NotSupportedException("Apple's container runtime does not support volume drivers.");

        var args = new List<string> { "volume", "create" };
        foreach (var (labelName, labelValue) in definition.Labels)
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

    internal override IReadOnlyList<string> BuildDeleteVolumeArguments(string name) => ["volume", "delete", name];

    internal override IReadOnlyList<string> BuildVolumeExistsArguments(string name) => ["volume", "inspect", name];

    internal override IReadOnlyList<string> BuildStartArguments(string id) => ["start", id];

    internal override IReadOnlyList<string> BuildStopArguments(string id) => ["stop", id];

    internal override IReadOnlyList<string> BuildRestartArguments(string id)
        => throw new NotSupportedException("Apple's container runtime does not support restart.");

    internal override IReadOnlyList<string> BuildPauseArguments(string id)
        => throw new NotSupportedException("Apple's container runtime does not support pause.");

    internal override IReadOnlyList<string> BuildUnpauseArguments(string id)
        => throw new NotSupportedException("Apple's container runtime does not support unpause.");

    internal override IReadOnlyList<string> BuildKillArguments(string id) => ["kill", id];

    internal override IReadOnlyList<string> BuildRemoveArguments(string id) => ["delete", "--force", id];

    internal override IReadOnlyList<string> BuildExistsArguments(string id) => ["inspect", id];

    internal override IReadOnlyList<string> BuildInspectArguments(string id) => ["inspect", id];

    internal override IReadOnlyList<string> BuildLogsArguments(string id) => ["logs", "--follow", id];

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
        };
    }

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(info);

        // The runtime reports the bindings it actually created, which is the only source that is correct for a
        // container adopted through ReuseId: its host ports were chosen by whichever run created it.
        if (info.Ports.Count > 0)
            return info.Ports;

        // Older CLI versions do not report 'publishedPorts', so fall back to what the definition asked for.
        var map = new Dictionary<int, int>();
        foreach (var port in definition.Ports)
            map[port.Port] = port.HostPort ?? port.Port;

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
