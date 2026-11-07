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
        definition.Mounts.AddVolume("config-volume", "/etc/app", readOnly: true);
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
        Assert.Equal("reuse-id", payload.Labels[ResourceLabels.ReuseId]);
        Assert.Contains("KEY=VALUE", payload.Env!);
        Assert.Equal("/bin/sh", Assert.Single(payload.Entrypoint!));
        Assert.Equal(["-c", "echo hi"], payload.Cmd);
        Assert.Contains("6379/tcp", payload.ExposedPorts!.Keys);
        Assert.Contains("5432/tcp", payload.ExposedPorts.Keys);
        Assert.Equal("", payload.HostConfig!.PortBindings!["6379/tcp"]![0]!.HostPort);
        Assert.Equal("127.0.0.1", payload.HostConfig.PortBindings["6379/tcp"]![0]!.HostIp);
        Assert.Equal("15432", payload.HostConfig.PortBindings["5432/tcp"]![0]!.HostPort);
        Assert.Equal(ContainerConfigurationHash.Compute(definition), payload.Labels[ResourceLabels.ConfigurationHash]);
        var mounts = Assert.IsType<List<DockerApiModels.Mount>>(payload.HostConfig.Mounts);
        Assert.Single(mounts, mount => mount.Type == "bind" && mount.Source == "/host" && mount.Target == "/container" && mount.ReadOnly);
        Assert.Single(mounts, mount => mount.Type == "volume" && mount.Source == "volume-name" && mount.Target == "/var/lib/data" && !mount.ReadOnly);
        Assert.Single(mounts, mount => mount.Type == "volume" && mount.Source == "config-volume" && mount.Target == "/etc/app" && mount.ReadOnly);
        Assert.Equal(string.Empty, payload.HostConfig.Tmpfs!["/tmpfs"]);
        Assert.True(payload.HostConfig.ReadonlyRootfs);
        Assert.Equal(536870912, payload.HostConfig.Memory);
        Assert.Equal(1_500_000_000, payload.HostConfig.NanoCpus);
        Assert.Equal("my-network", payload.HostConfig.NetworkMode);
        Assert.Equal(["my-alias"], payload.NetworkingConfig!.EndpointsConfig!["my-network"].Aliases);
    }

    [Fact]
    public void Build_OmitsTheHostAddressForAWindowsDaemon()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Ports.Add(6379);

        var payload = DockerApiCreateRequestBuilder.Build(definition, "redis:8", hostIpSupported: false);

        Assert.Null(payload.HostConfig!.PortBindings!["6379/tcp"]![0]!.HostIp);
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

    [Fact]
    public void Build_MapsTheCreationOptions()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"))
        {
            Privileged = true,
            StopTimeout = TimeSpan.FromSeconds(42.9),
            StopSignal = "SIGINT",
            Healthcheck = new ContainerHealthcheck("redis-cli ping")
            {
                Interval = TimeSpan.FromSeconds(1),
                Timeout = TimeSpan.FromMilliseconds(500),
                StartPeriod = TimeSpan.FromSeconds(10),
                Retries = 3,
            },
        };
        definition.CapAdd.Add("NET_ADMIN");
        definition.CapDrop.Add("ALL");
        definition.ExtraHosts.Add(ContainerExtraHost.ForHostGateway());
        definition.ExtraHosts.Add(new ContainerExtraHost("db.test", "10.0.0.1"));
        definition.Resources.SharedMemorySize = 1024 * 1024 * 1024;
        definition.Resources.Ulimits.Add(new ContainerUlimit("nofile", 1024, 2048));
        definition.Mounts.AddTmpfs("/scratch", size: 65536, mode: (UnixFileMode)Convert.ToInt32("1770", 8));
        definition.ExposedPorts.Add(new ContainerExposedPort(9000));
        definition.ExposedPorts.Add(new ContainerExposedPort(9001, ContainerPortProtocol.Udp));
        definition.Ports.Add(new ContainerPort(53) { Protocol = ContainerPortProtocol.Udp });

        var payload = DockerApiCreateRequestBuilder.Build(definition, "redis:8");

        Assert.True(payload.HostConfig!.Privileged);
        Assert.Equal(["NET_ADMIN"], payload.HostConfig.CapAdd);
        Assert.Equal(["ALL"], payload.HostConfig.CapDrop);
        Assert.Equal(["host.docker.internal:host-gateway", "db.test:10.0.0.1"], payload.HostConfig.ExtraHosts);
        Assert.Equal(1024 * 1024 * 1024, payload.HostConfig.ShmSize);
        var ulimit = Assert.Single(payload.HostConfig.Ulimits!);
        Assert.Equal(("nofile", 1024L, 2048L), (ulimit.Name, ulimit.Soft, ulimit.Hard));
        Assert.Equal("size=65536,mode=1770", payload.HostConfig.Tmpfs!["/scratch"]);
        Assert.Equal(["9000/tcp", "9001/udp", "53/udp"], payload.ExposedPorts!.Keys);
        Assert.Equal("", payload.HostConfig.PortBindings!["53/udp"]![0]!.HostPort);
        Assert.Equal(42, payload.StopTimeout);
        Assert.Equal("SIGINT", payload.StopSignal);
        Assert.Equal(["CMD-SHELL", "redis-cli ping"], payload.Healthcheck!.Test);
        Assert.Equal(1_000_000_000, payload.Healthcheck.Interval);
        Assert.Equal(500_000_000, payload.Healthcheck.Timeout);
        Assert.Equal(10_000_000_000, payload.Healthcheck.StartPeriod);
        Assert.Equal(3, payload.Healthcheck.Retries);
    }

    [Fact]
    public void Build_LeavesTheUnsetCreationOptionsOut()
    {
        var payload = DockerApiCreateRequestBuilder.Build(new ContainerDefinition(new RegistryImage("redis:8")), "redis:8");

        Assert.False(payload.HostConfig!.Privileged);
        Assert.Null(payload.HostConfig.CapAdd);
        Assert.Null(payload.HostConfig.ExtraHosts);
        Assert.Null(payload.HostConfig.ShmSize);
        Assert.Null(payload.HostConfig.Ulimits);
        Assert.Null(payload.Healthcheck);
        Assert.Null(payload.StopTimeout);
        Assert.Null(payload.ExposedPorts);
        Assert.Null(payload.NetworkingConfig);
        Assert.Null(payload.HostConfig.NetworkMode);
    }

    [Fact]
    public void Build_MapsTheHealthcheckCommandAndItsDisabling()
    {
        var command = new ContainerDefinition(new RegistryImage("redis:8")) { Healthcheck = new ContainerHealthcheck(["redis-cli", "ping"]) };
        var disabled = new ContainerDefinition(new RegistryImage("redis:8")) { Healthcheck = ContainerHealthcheck.Disabled };

        Assert.Equal(["CMD", "redis-cli", "ping"], DockerApiCreateRequestBuilder.Build(command, "redis:8").Healthcheck!.Test);
        Assert.Equal(["NONE"], DockerApiCreateRequestBuilder.Build(disabled, "redis:8").Healthcheck!.Test);
    }

    [Fact]
    public async Task Build_CreatesTheContainerOnTheFirstNetworkAndLeavesTheOthersForLater()
    {
        await using var temporaryNetwork = new NetworkDefinition().CreateNetwork();
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Network.Add(temporaryNetwork, "cache", "redis");
        definition.Network.Add("other-network", "second");

        var payload = DockerApiCreateRequestBuilder.Build(definition, "redis:8");

        Assert.Equal(temporaryNetwork.Name, payload.HostConfig!.NetworkMode);
        var endpoint = Assert.Single(payload.NetworkingConfig!.EndpointsConfig!);
        Assert.Equal(temporaryNetwork.Name, endpoint.Key);
        Assert.Equal(["cache", "redis"], endpoint.Value.Aliases);

        var (primary, additional) = definition.Network.GetNetworkPlan();
        Assert.Equal(temporaryNetwork.Name, primary!.Value.Name);
        var second = Assert.Single(additional);
        Assert.Equal("other-network", second.Network);
        Assert.Equal(["second"], second.Aliases);
        Assert.Null(second.TemporaryNetwork);
    }

    [Fact]
    public void Build_KeepsTheNetworkPropertyAsTheFirstNetwork()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Network.Network = "first";
        definition.Network.Add("second", "alias");

        var payload = DockerApiCreateRequestBuilder.Build(definition, "redis:8");

        Assert.Equal("first", payload.HostConfig!.NetworkMode);
        Assert.Null(payload.NetworkingConfig);
        Assert.Equal("second", Assert.Single(definition.Network.GetNetworkPlan().Additional).Network);
        Assert.Throws<ArgumentException>(() => definition.Network.Add("first"));
        Assert.Throws<ArgumentException>(() => definition.Network.Add("second"));
    }

    [Fact]
    public void Serialize_LetsTheCustomizerChangeTheRequest()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"))
        {
            DockerApiCreateRequestCustomizer = request =>
            {
                request["Domainname"] = "example.test";
                request["HostConfig"]!["OomScoreAdj"] = 500;
            },
        };

        var json = DockerApiCreateRequestBuilder.Serialize(definition, DockerApiCreateRequestBuilder.Build(definition, "redis:8"));

        using var document = JsonDocument.Parse(json);
        Assert.Equal("example.test", document.RootElement.GetProperty("Domainname").GetString());
        Assert.Equal(500, document.RootElement.GetProperty("HostConfig").GetProperty("OomScoreAdj").GetInt32());
        Assert.Equal("redis:8", document.RootElement.GetProperty("Image").GetString());
    }
}
