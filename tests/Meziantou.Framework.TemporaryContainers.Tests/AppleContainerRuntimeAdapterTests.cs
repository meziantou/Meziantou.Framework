using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class AppleContainerRuntimeAdapterTests
{
    private static AppleContainerRuntimeAdapter CreateAdapter()
        => Assert.IsAssignableTo<AppleContainerRuntimeAdapter>(ContainerRuntimeAdapter.Create(ContainerRuntime.AppleContainer, "container"));

    [Fact]
    public void UsesAppleVerbs()
    {
        var adapter = CreateAdapter();

        Assert.Equal(ContainerRuntime.AppleContainer, adapter.Runtime);
        Assert.False(adapter.SupportsPause);
        Assert.False(adapter.SupportsRestart);
        Assert.False(adapter.LogsIncludeTimestamps);
        Assert.Equal("delete --force abc", string.Join(' ', adapter.BuildRemoveArguments("abc")));
        Assert.Equal("copy src abc:/dst", string.Join(' ', adapter.BuildCopyToContainerArguments("abc", "src", "/dst")));
        Assert.Equal("copy abc:/src dst", string.Join(' ', adapter.BuildCopyFromContainerArguments("abc", "/src", "dst")));
        Assert.Equal("logs --follow abc", string.Join(' ', adapter.BuildLogsArguments("abc")));
    }

    [Fact]
    public void BuildCreateUsesAppleFlags()
    {
        var adapter = CreateAdapter();
        var definition = new ContainerDefinition(new RegistryImage("nginx"));
        definition.Ports.Add(8080);
        definition.Environment.Add("A", "1");

        var args = adapter.BuildCreateArguments(definition, "nginx");

        Assert.Equal("create", args[0]);
        Assert.Contains("--publish", args);
        Assert.Contains("8080:8080", args);
        Assert.Contains("A=1", args);
        Assert.Contains("nginx", args);
    }

    [Fact]
    public void ThrowsForUnsupportedOptions()
    {
        var adapter = CreateAdapter();

        var readOnly = new ContainerDefinition(new RegistryImage("nginx"));
        readOnly.Resources.ReadOnlyRootFilesystem = true;
        Assert.Throws<NotSupportedException>(() => adapter.BuildCreateArguments(readOnly, "nginx"));

        var tmpfs = new ContainerDefinition(new RegistryImage("nginx"));
        tmpfs.Mounts.AddTmpfs("/tmp");
        Assert.Throws<NotSupportedException>(() => adapter.BuildCreateArguments(tmpfs, "nginx"));

        Assert.Throws<NotSupportedException>(() => adapter.BuildPauseArguments("abc"));
        Assert.Throws<NotSupportedException>(() => adapter.BuildRestartArguments("abc"));
    }

    [Fact]
    public void ResolvesPortMapFromDefinition()
    {
        var adapter = CreateAdapter();
        var definition = new ContainerDefinition(new RegistryImage("nginx"));
        definition.Ports.Add(8080);
        definition.Ports.Add(15432, 5432);

        var info = new ContainerInfo { Id = "id", Name = "id" };
        var map = adapter.ResolvePortMap(info, definition);

        Assert.Equal(8080, map[8080]);
        Assert.Equal(15432, map[5432]);
    }
}
