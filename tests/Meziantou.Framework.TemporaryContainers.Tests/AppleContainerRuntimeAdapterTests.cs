using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class AppleContainerRuntimeAdapterTests
{
    private static AppleContainerRuntime CreateRuntime()
        => Assert.IsAssignableTo<AppleContainerRuntime>(ContainerRuntime.AppleContainer);

    [Fact]
    public void UsesAppleVerbs()
    {
        var runtime = CreateRuntime();

        Assert.False(runtime.SupportsPause);
        Assert.False(runtime.SupportsRestart);
        Assert.False(runtime.LogsIncludeTimestamps);
        Assert.Equal("delete --force abc", string.Join(' ', runtime.BuildRemoveArguments("abc")));
        Assert.Equal("copy src abc:/dst", string.Join(' ', runtime.BuildCopyToContainerArguments("abc", "src", "/dst")));
        Assert.Equal("copy abc:/src dst", string.Join(' ', runtime.BuildCopyFromContainerArguments("abc", "/src", "dst")));
        Assert.Equal("logs --follow abc", string.Join(' ', runtime.BuildLogsArguments("abc")));
        Assert.Equal("ls -q", string.Join(' ', runtime.BuildProbeArguments()));
    }

    [Fact]
    public void BuildCreateUsesAppleFlags()
    {
        var runtime = CreateRuntime();
        var definition = new ContainerDefinition(new RegistryImage("nginx"));
        definition.Ports.Add(9090, 8080);
        definition.Environment.Add("A", "1");

        var args = runtime.BuildCreateArguments(definition, "nginx");

        Assert.Equal("create", args[0]);
        Assert.Contains("--publish", args);
        Assert.Contains("9090:8080", args);
        Assert.Contains("A=1", args);
        Assert.Contains("nginx", args);
    }

    [Fact]
    public void ThrowsForUnsupportedOptions()
    {
        var runtime = CreateRuntime();

        var readOnly = new ContainerDefinition(new RegistryImage("nginx"));
        readOnly.Resources.ReadOnlyRootFilesystem = true;
        Assert.Throws<NotSupportedException>(() => runtime.BuildCreateArguments(readOnly, "nginx"));

        var tmpfs = new ContainerDefinition(new RegistryImage("nginx"));
        tmpfs.Mounts.AddTmpfs("/tmp");
        Assert.Throws<NotSupportedException>(() => runtime.BuildCreateArguments(tmpfs, "nginx"));

        Assert.Throws<NotSupportedException>(() => runtime.BuildPauseArguments("abc"));
        Assert.Throws<NotSupportedException>(() => runtime.BuildRestartArguments("abc"));
    }

    [Fact]
    public void ResolvesPortMapFromDefinitionWhenTheRuntimeReportsNoBinding()
    {
        var runtime = CreateRuntime();
        var definition = new ContainerDefinition(new RegistryImage("nginx"));
        definition.Ports.Add(8080);
        definition.Ports.Add(15432, 5432);

        var info = new ContainerInfo { Id = "id", Name = "id" };
        var map = runtime.ResolvePortMap(info, definition);

        Assert.Equal(8080, map[8080]);
        Assert.Equal(15432, map[5432]);
    }

    [Fact]
    public void ResolvesPortMapFromInspectWhenAvailable()
    {
        var runtime = CreateRuntime();

        // A container adopted through ReuseId: the definition asks for one mapping, the runtime reports another.
        var definition = new ContainerDefinition(new RegistryImage("nginx"));
        definition.Ports.Add(53437, 8080);

        var info = runtime.ParseInspect("""
            [{
              "id": "meziantou-tc-app",
              "status": { "state": "running" },
              "configuration": {
                "id": "meziantou-tc-app",
                "labels": { "meziantou.tc.reuse": "app" },
                "publishedPorts": [
                  { "containerPort": 8080, "hostPort": 53432, "proto": "tcp" }
                ]
              }
            }]
            """);

        Assert.Equal(53432, info.Ports[8080]);
        Assert.Equal("app", info.Labels["meziantou.tc.reuse"]);
        Assert.Equal(53432, runtime.ResolvePortMap(info, definition)[8080]);
    }
}
