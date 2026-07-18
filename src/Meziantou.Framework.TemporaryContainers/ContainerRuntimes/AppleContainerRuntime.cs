using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>CLI dialect for Apple's <c>container</c> runtime (macOS). Best-effort: verified against the documented CLI, not executed in CI on non-macOS hosts.</summary>
internal sealed class AppleContainerRuntime : ExecutableContainerRuntime
{
    private const string ReuseNamePrefix = "meziantou-tc-";

    public AppleContainerRuntime(string name)
        : base(name)
    {
    }

    internal override string ExecutableName => "container";

    internal override Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        // Apple's container runtime does not support random-port assignment and does not
        // report host port bindings in inspect output. Pre-assign free host ports so that
        // GetMappedPort works correctly after the container starts.
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

        return base.EnsureCreatedAsync(definition, cancellationToken);
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
                return ParseLoadedImage(loadResult.StandardOutput);

            case ExistingImage existing:
                return existing.ImageId;

            default:
                throw new NotSupportedException($"Image source '{source.GetType()}' is not supported.");
        }
    }

    internal override async Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken)
    {
        var name = GetReuseName(reuseId);
        var result = await Cli.RunBufferedAsync(["inspect", name], cancellationToken, allowNonZero: true).ConfigureAwait(false);
        return result.ExitCode == 0 ? name : null;
    }

    internal override IReadOnlyList<string> BuildCreateArguments(ContainerDefinition definition, string imageRef)
    {
        if (definition.Resources.ReadOnlyRootFilesystem)
            throw new NotSupportedException("Apple's container runtime does not support a read-only root filesystem.");
        if (definition.Network.Network is not null || definition.Network.Alias is not null)
            throw new NotSupportedException("Apple's container runtime does not support custom network options.");

        var args = new List<string> { "create" };

        var name = definition.ReuseId is { } reuseId ? GetReuseName(reuseId) : definition.Name;
        AddOption(args, "--name", name);
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
            Ports = new Dictionary<int, int>(),
            Labels = new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
    {
        // Apple's runtime does not report host port bindings in inspect and has no random-port discovery,
        // so the mapping is derived from the published ports (host defaults to the container port).
        var map = new Dictionary<int, int>();
        foreach (var port in definition.Ports)
            map[port.Port] = port.HostPort ?? port.Port;

        return map;
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
                args.Add("--volume");
                args.Add(bind.ReadOnly ? $"{bind.Source}:{bind.Target}:ro" : $"{bind.Source}:{bind.Target}");
                break;

            case VolumeMount volume:
                args.Add("--volume");
                args.Add($"{volume.Name}:{volume.Target}");
                break;

            case TmpfsMount:
                throw new NotSupportedException("Apple's container runtime does not support tmpfs mounts.");

            default:
                throw new NotSupportedException($"Mount type '{mount.GetType()}' is not supported.");
        }
    }

    private static string GetReuseName(string reuseId)
    {
        var builder = new StringBuilder(ReuseNamePrefix, ReuseNamePrefix.Length + reuseId.Length);
        foreach (var ch in reuseId)
            builder.Append(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '-' ? ch : '-');

        return builder.ToString();
    }

    private static string ParseLoadedImage(string output)
    {
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

        return output.Trim();
    }
}
