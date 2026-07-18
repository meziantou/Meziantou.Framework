using System.Globalization;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerApiCreateRequestBuilder
{
    public static DockerApiModels.CreateContainerRequest Build(ContainerDefinition definition, string imageRef)
    {
        var labels = definition.Labels.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        if (definition.ReuseId is { } reuseId)
            labels[DockerCreateArgumentBuilder.ReuseLabel] = reuseId;

        var hostConfig = new DockerApiModels.HostConfig
        {
            ReadonlyRootfs = definition.Resources.ReadOnlyRootFilesystem,
            Memory = definition.Resources.MemoryLimit,
            NanoCpus = definition.Resources.CpuLimit is { } cpu ? checked((long)(cpu * 1_000_000_000d)) : null,
            NetworkMode = definition.Network.Network,
        };

        Dictionary<string, DockerApiModels.EmptyObject>? exposedPorts = null;
        Dictionary<string, List<DockerPortBindingDto>?>? portBindings = null;
        if (definition.Ports.Count > 0)
        {
            exposedPorts = new Dictionary<string, DockerApiModels.EmptyObject>(definition.Ports.Count, StringComparer.Ordinal);
            portBindings = new Dictionary<string, List<DockerPortBindingDto>?>(definition.Ports.Count, StringComparer.Ordinal);
            foreach (var port in definition.Ports)
            {
                var key = string.Create(CultureInfo.InvariantCulture, $"{port.Port}/tcp");
                exposedPorts[key] = new DockerApiModels.EmptyObject();
                portBindings[key] =
                [
                    new DockerPortBindingDto
                    {
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
                    });
                    break;

                case TmpfsMount tmpfsMount:
                    tmpfs ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    tmpfs[tmpfsMount.Target] = string.Empty;
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
        if (!string.IsNullOrEmpty(definition.Network.Alias))
        {
            var network = definition.Network.Network ?? "bridge";
            networkingConfig = new DockerApiModels.NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, DockerApiModels.EndpointSettings>(StringComparer.Ordinal)
                {
                    [network] = new DockerApiModels.EndpointSettings
                    {
                        Aliases =
                        [
                            definition.Network.Alias,
                        ],
                    },
                },
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
        };
    }
}
