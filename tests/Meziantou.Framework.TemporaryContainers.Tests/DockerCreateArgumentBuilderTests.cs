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
        Assert.Contains("6379", args);
        Assert.Contains("15432:5432", args);
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

        Assert.Contains($"{DockerCreateArgumentBuilder.ReuseLabel}=my-reuse-id", args);
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
}
