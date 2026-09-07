using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class ContainerCleanupTests
{
    private static readonly SessionIdentity DeadRun = SessionIdentity.Current with { SessionId = "dead-session", ProcessStartTime = "0" };

    [Fact]
    public void ShouldRemove_IgnoresAResourceThisLibraryDidNotCreate()
    {
        var resource = new ManagedResource("id", new Dictionary<string, string>(StringComparer.Ordinal) { ["app"] = "something-else" });

        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions { Scope = ContainerCleanupScope.All }));
    }

    [Fact]
    public void ShouldRemove_KeepsTheResourcesOfTheCurrentRun()
    {
        var resource = CreateResource(SessionIdentity.Current);

        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions { Scope = ContainerCleanupScope.Orphaned }));
        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions { Scope = ContainerCleanupScope.All }));
    }

    [Fact]
    public void ShouldRemove_RemovesTheResourcesOfARunWhoseProcessIdWasReused()
    {
        // The process id is the one of this process, which is running, but the start time is not: the id belongs to
        // another process now, so the run that created the resource is over.
        var resource = CreateResource(DeadRun);

        Assert.True(ShouldRemove(resource, new ContainerCleanupOptions()));
    }

    [Fact]
    public void ShouldRemove_KeepsTheResourcesOfAnotherMachine()
    {
        var resource = CreateResource(DeadRun with { Host = "another-machine" });

        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions { Scope = ContainerCleanupScope.Orphaned }));
        Assert.True(ShouldRemove(resource, new ContainerCleanupOptions { Scope = ContainerCleanupScope.All }));
    }

    [Fact]
    public void ShouldRemove_KeepsTheResourcesOfARunningProcess()
    {
        var resource = CreateResource(SessionIdentity.Current with { SessionId = "another-session" });

        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions { Scope = ContainerCleanupScope.Orphaned }));
    }

    [Fact]
    public void ShouldRemove_KeepsTheReusedResourcesUnlessTheyAreAskedFor()
    {
        var resource = CreateResource(DeadRun, reuseId: "shared-database");

        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions()));
        Assert.True(ShouldRemove(resource, new ContainerCleanupOptions { IncludeReusedResources = true }));
    }

    [Fact]
    public void ShouldRemove_KeepsTheResourcesThatAreNotOldEnough()
    {
        var resource = CreateResource(DeadRun);

        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions { MinimumAge = TimeSpan.FromHours(1) }));
    }

    [Fact]
    public void ShouldRemove_RemovesTheResourcesThatAreOldEnough()
    {
        var resource = CreateResource(DeadRun);

        Assert.True(ShouldRemove(resource, new ContainerCleanupOptions { MinimumAge = TimeSpan.FromHours(1) }, DateTimeOffset.UtcNow.AddHours(2)));
    }

    [Fact]
    public void ShouldRemove_KeepsTheResourcesWhoseAgeIsUnknownWhenAnAgeIsRequired()
    {
        var labels = CreateResource(DeadRun).Labels.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        labels.Remove(ResourceLabels.CreatedAt);
        var resource = new ManagedResource("id", labels);

        Assert.False(ShouldRemove(resource, new ContainerCleanupOptions { MinimumAge = TimeSpan.FromSeconds(1) }, DateTimeOffset.UtcNow.AddHours(2)));
        Assert.True(ShouldRemove(resource, new ContainerCleanupOptions()));
    }

    [Fact]
    public void ContainersOfTheCurrentRunCarryTheSessionLabels()
    {
        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
        definition.Labels.Add("app", "mine");

        var labels = ResourceLabels.Build(definition.Labels, definition.ReuseId, definition.SessionOwned, definition.Identity);

        Assert.Equal("mine", labels["app"]);
        Assert.Equal("1", labels[ResourceLabels.Managed]);
        Assert.Equal(SessionIdentity.Current.SessionId, labels[ResourceLabels.SessionId]);
        Assert.Equal(SessionIdentity.Current.Host, labels[ResourceLabels.Host]);
        Assert.Equal(SessionIdentity.Current.ProcessId, labels[ResourceLabels.ProcessId]);
        Assert.DoesNotContain(ResourceLabels.ReuseId, labels);
    }

    [Fact]
    public void ADefinitionCannotOverwriteTheLibraryLabels()
    {
        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
        definition.Labels.Add(ResourceLabels.SessionId, "somebody-elses-session");

        var labels = ResourceLabels.Build(definition.Labels, definition.ReuseId, definition.SessionOwned, definition.Identity);

        Assert.Equal(SessionIdentity.Current.SessionId, labels[ResourceLabels.SessionId]);
    }

    [Fact]
    public void AReusedContainerIsNotPartOfTheSession()
    {
        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8")) { ReuseId = "shared-database" };

        var labels = ResourceLabels.Build(definition.Labels, definition.ReuseId, definition.SessionOwned, definition.Identity);

        // The container is shared with the runs that come after this one, so the reaper of this session must not take it down with it.
        Assert.DoesNotContain(ResourceLabels.SessionId, labels);
        Assert.Equal("shared-database", labels[ResourceLabels.ReuseId]);
    }

    [Fact]
    public void DockerVolumeInspectOutputIsReadAsManagedResources()
    {
        var output = """
            [
                {"Name":"meziantou-tc-1","Labels":{"meziantou.tc.managed":"1","meziantou.tc.session":"abc"}},
                {"Name":"other-volume","Labels":null}
            ]
            """;

        var resources = DockerVolumeInspectResult.Parse(output);

        Assert.Equal(2, resources.Count);
        Assert.Equal("meziantou-tc-1", resources[0].Id);
        Assert.Equal("abc", resources[0].Labels[ResourceLabels.SessionId]);
        Assert.Empty(resources[1].Labels);
    }

    [Fact]
    public void DockerVolumeInspectOutputThatIsNotJsonIsReadAsNoResource()
    {
        Assert.Empty(DockerVolumeInspectResult.Parse("Error: no such volume"));
        Assert.Empty(DockerVolumeInspectResult.Parse(""));
    }

    [Fact]
    public void DockerInspectOutputIsReadAsSeveralContainers()
    {
        var output = """
            [
                {"Id":"1111","Name":"/first","Config":{"Labels":{"meziantou.tc.managed":"1"}}},
                {"Id":"2222","Name":"/second","Config":{"Labels":{}}}
            ]
            """;

        var results = DockerContainerInfoParser.ParseInspectOutputs(output);

        Assert.Equal(["1111", "2222"], results.Select(result => result.Id));
        Assert.Equal("1", results[0].Labels[ResourceLabels.Managed]);
        Assert.Empty(DockerContainerInfoParser.ParseInspectOutputs("Error: No such object"));
    }

    [Fact]
    public void AppleListOutputOnlyReportsTheResourcesThisLibraryCreated()
    {
        var output = """
            [
                {"configuration":{"id":"buildkit","labels":{"com.apple.container.plugin":"builder"}},"id":"buildkit","status":{"state":"stopped"}},
                {"configuration":{"id":"mine","labels":{"meziantou.tc.managed":"1","meziantou.tc.session":"abc"}},"id":"mine","status":{"state":"running"}}
            ]
            """;

        var resource = Assert.Single(AppleContainerRuntime.ParseManagedResources(output));

        Assert.Equal("mine", resource.Id);
        Assert.Equal("abc", resource.Labels[ResourceLabels.SessionId]);
    }

    [Fact]
    public void AppleVolumeListOutputIsReadAsManagedResources()
    {
        var output = """
            [{"configuration":{"creationDate":"2026-09-06T20:38:43Z","driver":"local","labels":{"meziantou.tc.managed":"1"},"name":"meziantou-tc-probe"},"id":"meziantou-tc-probe"}]
            """;

        var resource = Assert.Single(AppleContainerRuntime.ParseManagedResources(output));

        Assert.Equal("meziantou-tc-probe", resource.Id);
    }

    private static ManagedResource CreateResource(SessionIdentity identity, string? reuseId = null)
    {
        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8")) { ReuseId = reuseId };
        return new ManagedResource("id", ResourceLabels.Build(definition.Labels, definition.ReuseId, definition.SessionOwned, identity));
    }

    private static bool ShouldRemove(ManagedResource resource, ContainerCleanupOptions options, DateTimeOffset? now = null)
        => ContainerRuntime.ShouldRemove(resource, options, now ?? DateTimeOffset.UtcNow);
}
