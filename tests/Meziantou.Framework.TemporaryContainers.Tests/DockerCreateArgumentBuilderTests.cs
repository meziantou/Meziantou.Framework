using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerCreateArgumentBuilderTests
{
    [Fact]
    public void BuildsExpectedArguments()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"))
        {
            Name = "my-container",
            Hostname = "my-host",
        };
        definition.Environment.Add("KEY", "VALUE");
        definition.Labels.Add("label", "labelvalue");
        definition.Ports.Add(6379);
        definition.Ports.Add(15432, 5432);
        definition.Mounts.AddBindMount("/host", "/container", readOnly: true);
        definition.Resources.MemoryLimit = 536870912;
        definition.Command.Add("--appendonly");

        var args = DockerCreateArgumentBuilder.Build(definition, "redis:8", "missing", quotedMountFieldsSupported: true);

        Assert.Equal("create", args[0]);
        Assert.Contains("--name", args);
        Assert.Contains("my-container", args);
        Assert.Contains("--hostname", args);
        Assert.Contains("KEY=VALUE", args);
        Assert.Contains("label=labelvalue", args);
        Assert.Contains("127.0.0.1::6379", args);
        Assert.Contains("127.0.0.1:15432:5432", args);
        Assert.Contains("type=bind,source=/host,target=/container,readonly", args);
        Assert.Contains("536870912b", args);
        Assert.Contains("--pull", args);
        Assert.Contains("missing", args);
        Assert.Contains("redis:8", args);
        Assert.Contains("--appendonly", args);
        Assert.True(args.IndexOf("redis:8") < args.IndexOf("--appendonly"));
    }

    [Fact]
    public void AddsReuseLabel()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"))
        {
            ReuseId = "my-reuse-id",
        };

        var args = DockerCreateArgumentBuilder.Build(definition, "redis:8", pullPolicyValue: null, quotedMountFieldsSupported: true);

        Assert.Contains($"{ResourceLabels.ReuseId}=my-reuse-id", args);
    }

    [Fact]
    public void AddsTheHashOfTheConfigurationToAReusedContainer()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8")) { ReuseId = "my-reuse-id" };
        var other = new ContainerDefinition(definition);
        other.Environment.Add("MODE", "other");

        var args = DockerCreateArgumentBuilder.Build(definition, "redis:8", pullPolicyValue: null, quotedMountFieldsSupported: true);

        Assert.Contains($"{ResourceLabels.ConfigurationHash}={ContainerConfigurationHash.Compute(definition)}", args);
        Assert.NotEqual(ContainerConfigurationHash.Compute(definition), ContainerConfigurationHash.Compute(other));
    }

    [Fact]
    public void OmitsTheHostAddressWhenTheRuntimeCannotBindOne()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Ports.Add(6379);

        var args = DockerCreateArgumentBuilder.Build(definition, "redis:8", pullPolicyValue: null, quotedMountFieldsSupported: true, hostIpSupported: false);

        Assert.Contains("6379", args);
    }

    [Fact]
    public void MapsVolumeAndTmpfsMounts()
    {
        var definition = new ContainerDefinition(new RegistryImage("alpine"));
        definition.Mounts.AddVolume("data", "/var/lib/data");
        definition.Mounts.AddVolume("config", "/etc/app", readOnly: true);
        definition.Mounts.AddTmpfs("/scratch");

        var args = DockerCreateArgumentBuilder.Build(definition, "alpine", pullPolicyValue: null, quotedMountFieldsSupported: true);

        Assert.Contains("type=volume,source=data,target=/var/lib/data", args);
        Assert.Contains("type=volume,source=config,target=/etc/app,readonly", args);
        Assert.Contains("--tmpfs", args);
        Assert.Contains("/scratch", args);
    }

    [Fact]
    public void QuotesMountFieldsContainingASeparator()
    {
        var definition = new ContainerDefinition(new RegistryImage("alpine"));
        definition.Mounts.AddBindMount("""/host/a,b""", """/container/c"d""", readOnly: true);

        var args = DockerCreateArgumentBuilder.Build(definition, "alpine", pullPolicyValue: null, quotedMountFieldsSupported: true);

        Assert.Contains("""type=bind,"source=/host/a,b","target=/container/c""d",readonly""", args);
    }

    [Fact]
    public void ThrowsWhenAMountFieldCannotBeQuoted()
    {
        var definition = new ContainerDefinition(new RegistryImage("alpine"));
        definition.Mounts.AddBindMount("/host/a,b", "/container");

        Assert.Throws<NotSupportedException>(() => DockerCreateArgumentBuilder.Build(definition, "alpine", pullPolicyValue: null, quotedMountFieldsSupported: false));
    }

    [Fact]
    public void MapsEntrypointAndCommand()
    {
        var definition = new ContainerDefinition(new RegistryImage("alpine"));
        definition.Entrypoint.Add("/bin/sh");
        definition.Entrypoint.Add("-c");
        definition.Command.Add("echo hi");

        var args = DockerCreateArgumentBuilder.Build(definition, "alpine", pullPolicyValue: null, quotedMountFieldsSupported: true);

        Assert.Contains("--entrypoint", args);
        Assert.Contains("/bin/sh", args);
        var imageIndex = args.IndexOf("alpine");
        Assert.True(args.IndexOf("-c") > imageIndex);
        Assert.True(args.IndexOf("echo hi") > imageIndex);
    }

    [Fact]
    public void AddsTheCreationOptions()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"))
        {
            Privileged = true,
            StopTimeout = TimeSpan.FromSeconds(42),
            StopSignal = "SIGINT",
            Healthcheck = new ContainerHealthcheck(["redis-cli", "it's"]) { Interval = TimeSpan.FromSeconds(1.5), Timeout = TimeSpan.FromSeconds(2), StartPeriod = TimeSpan.FromSeconds(3), Retries = 4 },
        };
        definition.CapAdd.Add("NET_ADMIN");
        definition.CapDrop.Add("ALL");
        definition.ExtraHosts.Add(ContainerExtraHost.ForHostGateway());
        definition.Resources.SharedMemorySize = 1024;
        definition.Resources.Ulimits.Add(new ContainerUlimit("nofile", 1024));
        definition.Mounts.AddTmpfs("/scratch", size: 65536, mode: (UnixFileMode)Convert.ToInt32("1777", 8));
        definition.ExposedPorts.Add(new ContainerExposedPort(9001, ContainerPortProtocol.Udp));
        definition.Ports.Add(new ContainerPort(53) { Protocol = ContainerPortProtocol.Udp });
        definition.AdditionalCreateArguments.Add("--init");
        definition.Command.Add("redis-server");

        var args = string.Join(' ', DockerCreateArgumentBuilder.Build(definition, "redis:8", pullPolicyValue: null, quotedMountFieldsSupported: true));

        Assert.Contains(" --shm-size 1024b ", args);
        Assert.Contains(" --ulimit nofile=1024:1024 ", args);
        Assert.Contains(" --privileged ", args);
        Assert.Contains(" --cap-add NET_ADMIN --cap-drop ALL ", args);
        Assert.Contains(" --add-host host.docker.internal:host-gateway ", args);
        Assert.Contains(" --health-cmd 'redis-cli' 'it'\\''s' --health-interval 1500ms --health-timeout 2000ms --health-start-period 3000ms --health-retries 4 ", args);
        Assert.Contains(" --stop-timeout 42 --stop-signal SIGINT ", args);
        Assert.Contains(" -p 127.0.0.1::53/udp ", args);
        Assert.Contains(" --expose 9001/udp ", args);
        Assert.Contains(" --tmpfs /scratch:size=65536,mode=1777 ", args);
        Assert.EndsWith(" --init redis:8 redis-server", args);
    }

    [Fact]
    public void DisablesTheHealthcheck()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8")) { Healthcheck = ContainerHealthcheck.Disabled };

        var args = DockerCreateArgumentBuilder.Build(definition, "redis:8", pullPolicyValue: null, quotedMountFieldsSupported: true);

        Assert.Contains("--no-healthcheck", args);
        Assert.DoesNotContain("--health-cmd", args);
    }

    [Fact]
    public void CreatesTheContainerOnTheFirstNetworkOnly()
    {
        var definition = new ContainerDefinition(new RegistryImage("redis:8"));
        definition.Network.Add("first", "a", "b");
        definition.Network.Add("second", "c");

        var args = string.Join(' ', DockerCreateArgumentBuilder.Build(definition, "redis:8", pullPolicyValue: null, quotedMountFieldsSupported: true));

        Assert.Contains(" --network first --network-alias a --network-alias b ", args);
        Assert.DoesNotContain("second", args);
    }
}
