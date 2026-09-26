using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Tomlyn.Tests;

#pragma warning disable CA1002 // Test models use List<T> on purpose
#pragma warning disable MA0048 // File name must match type name

public sealed class OutOfOrderSubtableRoot
{
    [JsonPropertyName("msbuild")]
    public OutOfOrderSubtableMsBuild MSBuild { get; } = new();

    [JsonPropertyName("github")]
    public OutOfOrderSubtableGitHub GitHub { get; } = new();
}

public sealed class OutOfOrderSubtableMsBuild
{
    public string Project { get; set; } = string.Empty;

    public Dictionary<string, object> Properties { get; } = new();
}

public sealed class OutOfOrderSubtableGitHub
{
    public string User { get; set; } = string.Empty;

    public string Repo { get; set; } = string.Empty;
}

public sealed class NestedTableArrayRoot
{
    public List<NestedTableArrayCommand>? Command { get; set; }
}

public sealed class NestedTableArrayCommand
{
    public string? CommandId { get; set; }

    public List<NestedTableArraySlash>? Slash { get; set; }
}

public sealed class NestedTableArraySlash
{
    public string? Path { get; set; }

    public string? Help { get; set; }

    public NestedTableArrayArgs? Args { get; set; }
}

public sealed class NestedTableArrayArgs
{
    public string? Surface { get; set; }
}

[TomlSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate)]
[TomlSerializable(typeof(OutOfOrderSubtableRoot))]
[TomlSerializable(typeof(NestedTableArrayRoot))]
internal sealed partial class TestOutOfOrderSubtableContext : TomlSerializerContext
{
}

public class NewApiOutOfOrderSubtableTests
{
    private const string SampleToml =
        """
        [msbuild]
        project = "HelloWorld.csproj"

        [github]
        user = "u"
        repo = "r"

        [msbuild.properties]
        PublishReadyToRun = false
        """;

    private const string NestedTableArrayToml =
        """
        intent_catalog_schema_version = 2

        [[command]]
        command_id = "open_file"

        [[command.slash]]
        path = "/file open"
        help = "Open file"
        args = { surface = "editor" }

        [[command]]
        command_id = "open_folder"

        [[command.slash]]
        path = "/file pick"
        help = "Pick file"
        """;

    [Fact]
    public void Reflection_AllowsOutOfOrderSubtableExtensions()
    {
        var result = TomlSerializer.Deserialize<OutOfOrderSubtableRoot>(SampleToml, new TomlSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
            SourceName = "repro.toml",
        });

        Assert.NotNull(result);
        Assert.Equal("HelloWorld.csproj", result!.MSBuild.Project);
        Assert.Equal("u", result.GitHub.User);
        Assert.Equal("r", result.GitHub.Repo);
        Assert.Equal(false, result.MSBuild.Properties["PublishReadyToRun"]);
    }

    [Fact]
    public void SourceGenerated_AllowsOutOfOrderSubtableExtensions()
    {
        var context = TestOutOfOrderSubtableContext.Default;
        var result = TomlSerializer.Deserialize(SampleToml, context.OutOfOrderSubtableRoot);

        Assert.NotNull(result);
        Assert.Equal("HelloWorld.csproj", result!.MSBuild.Project);
        Assert.Equal("u", result.GitHub.User);
        Assert.Equal("r", result.GitHub.Repo);
        Assert.Equal(false, result.MSBuild.Properties["PublishReadyToRun"]);
    }

    [Fact]
    public void TypedDictionary_AllowsOutOfOrderSubtableExtensions()
    {
        var result = TomlSerializer.Deserialize<Dictionary<string, object>>(SampleToml, new TomlSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            SourceName = "repro.toml",
        });

        Assert.NotNull(result);
        Assert.True(result!.TryGetValue("msbuild", out var rawMsBuild));
        Assert.IsType<TomlTable>(rawMsBuild);

        var msbuild = (TomlTable)rawMsBuild!;
        Assert.Equal("HelloWorld.csproj", msbuild["project"]);
        Assert.IsType<TomlTable>(msbuild["properties"]);

        var properties = (TomlTable)msbuild["properties"];
        Assert.Equal(false, properties["PublishReadyToRun"]);
    }

    [Fact]
    public void Reflection_AllowsNestedTableArraysInsideTableArrays()
    {
        var result = TomlSerializer.Deserialize<NestedTableArrayRoot>(NestedTableArrayToml, new TomlSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            SourceName = "issue-124.toml",
        });

        AssertNestedTableArrayResult(result);
    }

    [Fact]
    public void SourceGenerated_AllowsNestedTableArraysInsideTableArrays()
    {
        var context = TestOutOfOrderSubtableContext.Default;
        var result = TomlSerializer.Deserialize(NestedTableArrayToml, context.NestedTableArrayRoot);

        AssertNestedTableArrayResult(result);
    }

    private static void AssertNestedTableArrayResult(NestedTableArrayRoot? result)
    {
        Assert.NotNull(result);
        Assert.HasCount(2, result.Command);

        var firstCommand = result.Command[0];
        Assert.Equal("open_file", firstCommand.CommandId);
        Assert.HasCount(1, firstCommand.Slash);
        var firstSlash = firstCommand.Slash[0];
        Assert.Equal("/file open", firstSlash.Path);
        Assert.Equal("Open file", firstSlash.Help);
        Assert.NotNull(firstSlash.Args);
        Assert.Equal("editor", firstSlash.Args.Surface);

        var secondCommand = result.Command[1];
        Assert.Equal("open_folder", secondCommand.CommandId);
        Assert.HasCount(1, secondCommand.Slash);
        var secondSlash = secondCommand.Slash[0];
        Assert.Equal("/file pick", secondSlash.Path);
        Assert.Equal("Pick file", secondSlash.Help);
    }
}
