using System.Text.Json;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerApiCreateRequestBuilderTests
{
    [Fact]
    public void Build_MapsContainerDefinitionToDockerApiPayload()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"))
        {
            Name = "my-container",
            Hostname = "my-host",
            User = "1000:1000",
            WorkingDirectory = "/work",
            ReuseId = "reuse-id",
        };
        definition.Environment.Add("KEY", "VALUE");
        definition.Labels.Add("label", "labelvalue");
        definition.Ports.Add(6379);
        definition.Ports.Add(15432, 5432);
        definition.Mounts.AddBindMount("/host", "/container", readOnly: true);
        definition.Mounts.AddVolume("volume-name", "/var/lib/data");
        definition.Mounts.AddTmpfs("/tmpfs");
        definition.Network.Network = "my-network";
        definition.Network.Alias = "my-alias";
        definition.Resources.ReadOnlyRootFilesystem = true;
        definition.Resources.MemoryLimit = 536870912;
        definition.Resources.CpuLimit = 1.5d;
        definition.Entrypoint.Add("/bin/sh");
        definition.Entrypoint.Add("-c");
        definition.Command.Add("echo hi");

        var payload = DockerApiCreateRequestBuilder.Build(definition, "redis:8");

        Assert.Equal("redis:8", payload.Image);
        Assert.Equal("my-host", payload.Hostname);
        Assert.Equal("1000:1000", payload.User);
        Assert.Equal("/work", payload.WorkingDir);
        Assert.Equal("labelvalue", payload.Labels!["label"]);
        Assert.Equal("reuse-id", payload.Labels[DockerCreateArgumentBuilder.ReuseLabel]);
        Assert.Contains("KEY=VALUE", payload.Env!);
        Assert.Equal("/bin/sh", Assert.Single(payload.Entrypoint!));
        Assert.Equal(["-c", "echo hi"], payload.Cmd);
        Assert.Contains("6379/tcp", payload.ExposedPorts!.Keys);
        Assert.Contains("5432/tcp", payload.ExposedPorts.Keys);
        Assert.Equal("", payload.HostConfig!.PortBindings!["6379/tcp"]![0]!.HostPort);
        Assert.Equal("15432", payload.HostConfig.PortBindings["5432/tcp"]![0]!.HostPort);
        var mounts = Assert.IsType<List<DockerApiModels.Mount>>(payload.HostConfig.Mounts);
        Assert.Single(mounts, mount => mount.Type == "bind" && mount.Source == "/host" && mount.Target == "/container" && mount.ReadOnly);
        Assert.Single(mounts, mount => mount.Type == "volume" && mount.Source == "volume-name" && mount.Target == "/var/lib/data");
        Assert.Equal(string.Empty, payload.HostConfig.Tmpfs!["/tmpfs"]);
        Assert.True(payload.HostConfig.ReadonlyRootfs);
        Assert.Equal(536870912, payload.HostConfig.Memory);
        Assert.Equal(1_500_000_000, payload.HostConfig.NanoCpus);
        Assert.Equal("my-network", payload.HostConfig.NetworkMode);
        Assert.Equal(["my-alias"], payload.NetworkingConfig!.EndpointsConfig!["my-network"].Aliases);
    }

    [Fact]
    public void Build_UsesSourceGeneratedSerializableTypeForExposedPorts()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Ports.Add(6379);

        var payload = DockerApiCreateRequestBuilder.Build(definition, "redis:8");
        var json = JsonSerializer.Serialize(payload, DockerApiJsonContext.Default.CreateContainerRequest);

        Assert.Contains("\"ExposedPorts\":{\"6379/tcp\":{}}", json);
    }
}
