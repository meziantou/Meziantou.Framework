using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerApiCreateRequestBuilder
{
    /// <param name="definition">The container definition.</param>
    /// <param name="imageRef">The image to create the container from.</param>
    /// <param name="hostIpSupported">Whether the daemon can publish a port on a specific host address, which a daemon running Windows containers cannot.</param>
    public static DockerApiModels.CreateContainerRequest Build(ContainerDefinition definition, string imageRef, bool hostIpSupported = true)
    {
        var labels = ResourceLabels.Build(definition);

        var hostConfig = new DockerApiModels.HostConfig
        {
            ReadonlyRootfs = definition.Resources.ReadOnlyRootFilesystem,
            Memory = definition.Resources.MemoryLimit,
            NanoCpus = definition.Resources.CpuLimit is { } cpu ? checked((long)(cpu * 1_000_000_000d)) : null,
            Privileged = definition.Privileged,
            CapAdd = definition.CapAdd.Count > 0 ? [.. definition.CapAdd] : null,
            CapDrop = definition.CapDrop.Count > 0 ? [.. definition.CapDrop] : null,
            ExtraHosts = definition.ExtraHosts.Count > 0 ? [.. definition.ExtraHosts.Select(static host => host.Format())] : null,
            ShmSize = definition.Resources.SharedMemorySize,
            Ulimits = definition.Resources.Ulimits.Count > 0
                ? [.. definition.Resources.Ulimits.Select(static ulimit => new DockerApiModels.Ulimit { Name = ulimit.Name, Soft = ulimit.Soft, Hard = ulimit.Hard })]
                : null,
        };

        var (primaryNetwork, _) = definition.Network.GetNetworkPlan();
        hostConfig.NetworkMode = primaryNetwork?.Name;

        Dictionary<string, DockerApiModels.EmptyObject>? exposedPorts = null;
        Dictionary<string, List<DockerPortBindingDto>?>? portBindings = null;
        foreach (var exposedPort in definition.ExposedPorts)
        {
            exposedPorts ??= new Dictionary<string, DockerApiModels.EmptyObject>(StringComparer.Ordinal);
            exposedPorts[string.Create(CultureInfo.InvariantCulture, $"{exposedPort.Port}/{ContainerPort.GetProtocolName(exposedPort.Protocol)}")] = DockerApiModels.EmptyObject.Instance;
        }

        if (definition.Ports.Count > 0)
        {
            exposedPorts ??= new Dictionary<string, DockerApiModels.EmptyObject>(definition.Ports.Count, StringComparer.Ordinal);
            portBindings = new Dictionary<string, List<DockerPortBindingDto>?>(definition.Ports.Count, StringComparer.Ordinal);
            foreach (var port in definition.Ports)
            {
                var key = string.Create(CultureInfo.InvariantCulture, $"{port.Port}/{port.ProtocolName}");
                exposedPorts[key] = DockerApiModels.EmptyObject.Instance;
                portBindings[key] =
                [
                    new DockerPortBindingDto
                    {
                        HostIp = hostIpSupported ? port.HostIp : null,
                        HostPort = port.HostPort?.ToString(CultureInfo.InvariantCulture) ?? "",
                    },
                ];
            }
        }

        hostConfig.PortBindings = portBindings;

        List<DockerApiModels.Mount>? mounts = null;
        Dictionary<string, string>? tmpfs = null;
        foreach (var mount in definition.Mounts)
        {
            switch (mount)
            {
                case BindMount bind:
                    mounts ??= [];
                    mounts.Add(new DockerApiModels.Mount
                    {
                        Type = "bind",
                        Source = bind.Source,
                        Target = bind.Target,
                        ReadOnly = bind.ReadOnly,
                    });
                    break;

                case VolumeMount volume:
                    mounts ??= [];
                    mounts.Add(new DockerApiModels.Mount
                    {
                        Type = "volume",
                        Source = volume.Name,
                        Target = volume.Target,
                        ReadOnly = volume.ReadOnly,
                    });
                    break;

                case OwnedVolumeMount owned:
                    mounts ??= [];
                    mounts.Add(new DockerApiModels.Mount
                    {
                        Type = "volume",
                        Source = owned.Volume.Name,
                        Target = owned.Target,
                        ReadOnly = owned.ReadOnly,
                    });
                    break;

                case TmpfsMount tmpfsMount:
                    tmpfs ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    tmpfs[tmpfsMount.Target] = tmpfsMount.FormatOptions();
                    break;

                default:
                    throw new NotSupportedException($"Mount type '{mount.GetType()}' is not supported.");
            }
        }

        hostConfig.Mounts = mounts;
        hostConfig.Tmpfs = tmpfs;

        var entrypoint = definition.Entrypoint.ToArray();
        var command = new List<string>(definition.Command.Count + Math.Max(0, entrypoint.Length - 1));
        if (entrypoint.Length > 1)
            command.AddRange(entrypoint[1..]);

        command.AddRange(definition.Command);

        DockerApiModels.NetworkingConfig? networkingConfig = null;
        if (primaryNetwork is { Aliases.Count: > 0 } endpoint)
        {
            networkingConfig = new DockerApiModels.NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, DockerApiModels.EndpointSettings>(StringComparer.Ordinal)
                {
                    [endpoint.Name ?? "bridge"] = new DockerApiModels.EndpointSettings
                    {
                        Aliases = [.. endpoint.Aliases],
                    },
                },
            };
        }

        DockerApiModels.Healthcheck? healthcheck = null;
        if (definition.Healthcheck is { } containerHealthcheck)
        {
            healthcheck = new DockerApiModels.Healthcheck
            {
                Test = containerHealthcheck.GetTest(),
                Interval = ToNanoseconds(containerHealthcheck.Interval),
                Timeout = ToNanoseconds(containerHealthcheck.Timeout),
                StartPeriod = ToNanoseconds(containerHealthcheck.StartPeriod),
                Retries = containerHealthcheck.Retries,
            };
        }

        return new DockerApiModels.CreateContainerRequest
        {
            Image = imageRef,
            Hostname = definition.Hostname,
            User = definition.User,
            WorkingDir = definition.WorkingDirectory,
            Labels = labels.Count == 0 ? null : labels,
            Env = definition.Environment.Select(static pair => pair.Key + "=" + pair.Value).ToArray(),
            Entrypoint = entrypoint.Length > 0 ? [entrypoint[0]] : null,
            Cmd = command.Count > 0 ? [.. command] : null,
            ExposedPorts = exposedPorts,
            HostConfig = hostConfig,
            NetworkingConfig = networkingConfig,
            Healthcheck = healthcheck,
            StopTimeout = definition.StopTimeout is { } stopTimeout ? checked((int)stopTimeout.TotalSeconds) : null,
            StopSignal = definition.StopSignal,
        };
    }

    /// <summary>Serializes the request, and lets <see cref="ContainerDefinition.DockerApiCreateRequestCustomizer"/> change it.</summary>
    public static string Serialize(ContainerDefinition definition, DockerApiModels.CreateContainerRequest request)
    {
        if (definition.DockerApiCreateRequestCustomizer is not { } customizer)
            return JsonSerializer.Serialize(request, DockerApiJsonContext.Default.CreateContainerRequest);

        var json = JsonSerializer.SerializeToNode(request, DockerApiJsonContext.Default.CreateContainerRequest) as JsonObject
            ?? throw new InvalidOperationException("The create request did not serialize to a JSON object.");

        customizer(json);
        return json.ToJsonString();
    }

    private static long? ToNanoseconds(TimeSpan? value) => value is { } duration ? checked(duration.Ticks * 100) : null;
}
