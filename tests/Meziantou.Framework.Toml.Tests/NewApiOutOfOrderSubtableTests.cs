using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable CA1002 // Test models use List<T> on purpose
#pragma warning disable MA0048 // File name must match type name
#pragma warning disable CA1819 // Test models use arrays on purpose

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

public sealed class CargoManifest
{
    public CargoPackage? Package { get; set; }

    public List<CargoTarget>? Bin { get; set; }

    public CargoTarget[]? Example { get; set; }

    public Dictionary<string, string>? Dependencies { get; set; }
}

public sealed class CargoPackage
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}

public sealed class CargoTarget
{
    public string? Name { get; set; }

    public CargoTargetSettings? Settings { get; set; }
}

public sealed class CargoTargetSettings
{
    public string? Opt { get; set; }
}

public sealed class SplitShapeRoot
{
    public SplitShape? Shape { get; set; }

    public SplitSection? Other { get; set; }
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "type")]
[TomlDerivedType(typeof(SplitCircle), "circle")]
public class SplitShape
{
    public SplitPoint? Center { get; set; }
}

public sealed class SplitCircle : SplitShape
{
    public int Radius { get; set; }
}

public sealed class SplitPoint
{
    public int X { get; set; }
}

public sealed class SplitSection
{
    public int Value { get; set; }
}

public sealed class SplitSectionsRoot
{
    public SplitSectionWithChild? A { get; set; }

    public SplitSection? B { get; set; }
}

public sealed class SplitSectionWithChild
{
    public int Y { get; set; }

    public SplitSection? Child { get; set; }
}

public sealed class SplitExtensionDataRoot
{
    public SplitSection? Other { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed record SplitRecordRoot(SplitRecordSection A, SplitSection B);

public sealed record SplitRecordSection(int Y, SplitSection Child);

[TomlSourceGenerationOptions(
    PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower,
    PreferredObjectCreationHandling = TomlObjectCreationHandling.Populate)]
[TomlSerializable(typeof(OutOfOrderSubtableRoot))]
[TomlSerializable(typeof(NestedTableArrayRoot))]
[TomlSerializable(typeof(CargoManifest))]
[TomlSerializable(typeof(SplitShapeRoot))]
[TomlSerializable(typeof(SplitSectionsRoot))]
[TomlSerializable(typeof(SplitExtensionDataRoot))]
[TomlSerializable(typeof(SplitRecordRoot))]
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

    // Arrays of tables reopened after other tables, as in Cargo manifests
    private const string CargoToml =
        """
        [package]
        name = "demo"

        [[bin]]
        name = "first"

        [dependencies]
        serde = "1"

        [[example]]
        name = "ex1"

        [[bin]]
        name = "second"

        [bin.settings]
        opt = "3"

        [[example]]
        name = "ex2"
        """;

    [Fact]
    public void Reflection_AllowsOutOfOrderSubtableExtensions()
    {
        var result = TomlSerializer.Deserialize<OutOfOrderSubtableRoot>(SampleToml, new TomlSerializerOptions
        {
            PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower,
            PreferredObjectCreationHandling = TomlObjectCreationHandling.Populate,
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
            PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower,
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
            PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower,
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

    [Fact]
    public void Reflection_AllowsReopenedTableArrays()
    {
        var result = TomlSerializer.Deserialize<CargoManifest>(CargoToml, new TomlSerializerOptions { PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower });

        AssertCargoManifest(result);
    }

    [Fact]
    public void SourceGenerated_AllowsReopenedTableArrays()
    {
        var result = TomlSerializer.Deserialize(CargoToml, TestOutOfOrderSubtableContext.Default.CargoManifest);

        AssertCargoManifest(result);
    }

    [Fact]
    public void TomlTable_AllowsReopenedTableArrays()
    {
        var result = TomlSerializer.Deserialize<TomlTable>(CargoToml)!;

        var bins = (TomlTableArray)result["bin"];
        Assert.HasCount(2, bins);
        Assert.Equal("first", bins[0]["name"]);
        Assert.Equal("second", bins[1]["name"]);
        Assert.Equal("3", ((TomlTable)bins[1]["settings"])["opt"]);
        Assert.HasCount(2, (TomlTableArray)result["example"]);
        Assert.Equal(["package", "bin", "dependencies", "example"], result.Keys);
    }

    [Fact]
    public void TypedDictionary_AllowsReopenedTableArrays()
    {
        var result = TomlSerializer.Deserialize<Dictionary<string, object>>(CargoToml)!;

        var bins = (TomlTableArray)result["bin"];
        Assert.HasCount(2, bins);
        Assert.Equal("3", ((TomlTable)bins[1]["settings"])["opt"]);
    }

    [Fact]
    public void TomlTable_SubtableOfClosedTableArray_ExtendsLastElement()
    {
        var result = TomlSerializer.Deserialize<TomlTable>(
            """
            [[fruit]]
            name = "apple"

            [[veg]]
            name = "carrot"

            [fruit.physical]
            color = "red"
            """)!;

        var fruit = Assert.Single((TomlTableArray)result["fruit"]);
        Assert.Equal("apple", fruit["name"]);
        Assert.Equal("red", ((TomlTable)fruit["physical"])["color"]);
    }

    [Fact]
    public void TomlTable_DottedKeysSplitByAnotherKey_AreMerged()
    {
        var result = TomlSerializer.Deserialize<TomlTable>("a.x = 1\nb = 2\na.y = 3\nt = {c.x = 1, d = 2, c.y = 3}\n")!;

        Assert.Equal(["a", "b", "t"], result.Keys);
        var a = (TomlTable)result["a"];
        Assert.Equal(1L, a["x"]);
        Assert.Equal(3L, a["y"]);
        var c = (TomlTable)((TomlTable)result["t"])["c"];
        Assert.Equal(1L, c["x"]);
        Assert.Equal(3L, c["y"]);
    }

    // The same naming policy as TestOutOfOrderSubtableContext
    private static readonly TomlSerializerOptions SnakeCaseOptions = new() { PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower };

    [Fact]
    public void Polymorphic_SplitTable_KeepsDerivedTypeAndMembers()
    {
        const string Toml = "[shape]\ntype = \"circle\"\nradius = 2\n[other]\nvalue = 1\n[shape.center]\nx = 3\n";

        AssertShape(TomlSerializer.Deserialize<SplitShapeRoot>(Toml, SnakeCaseOptions));
        AssertShape(TomlSerializer.Deserialize(Toml, TestOutOfOrderSubtableContext.Default.SplitShapeRoot));

        static void AssertShape(SplitShapeRoot? result)
        {
            var circle = Assert.IsType<SplitCircle>(result?.Shape);
            Assert.Equal(2, circle.Radius);
            Assert.Equal(3, circle.Center?.X);
            Assert.Equal(1, result!.Other?.Value);
        }
    }

    [Fact]
    public void LastWins_SplitTable_KeepsAllFragments()
    {
        const string Toml = "[A]\nY = 1\n[B]\nValue = 2\n[A.Child]\nValue = 3\n";
        var options = new TomlSerializerOptions { DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins };

        var result = TomlSerializer.Deserialize<SplitSectionsRoot>(Toml, options);

        Assert.Equal(1, result?.A?.Y);
        Assert.Equal(3, result?.A?.Child?.Value);
        Assert.Equal(2, result?.B?.Value);
    }

    [Fact]
    public void ExtensionData_SplitTable_KeepsAllFragments()
    {
        const string Toml = "[unknown]\nx = 1\n[other]\nvalue = 2\n[unknown.sub]\ny = 3\n";

        AssertExtensionData(TomlSerializer.Deserialize<SplitExtensionDataRoot>(Toml, SnakeCaseOptions));
        AssertExtensionData(TomlSerializer.Deserialize(Toml, TestOutOfOrderSubtableContext.Default.SplitExtensionDataRoot));

        static void AssertExtensionData(SplitExtensionDataRoot? result)
        {
            Assert.Equal(2, result?.Other?.Value);
            var unknown = Assert.IsType<TomlTable>(result?.Extra?["unknown"]);
            Assert.Equal(1L, unknown["x"]);
            Assert.Equal(3L, ((TomlTable)unknown["sub"])["y"]);
        }
    }

    [Fact]
    public void ConstructorParameters_SplitTable_AreBound()
    {
        const string Toml = "[a]\ny = 1\n[b]\nvalue = 2\n[a.child]\nvalue = 3\n";

        AssertRecord(TomlSerializer.Deserialize<SplitRecordRoot>(Toml, SnakeCaseOptions));
        AssertRecord(TomlSerializer.Deserialize(Toml, TestOutOfOrderSubtableContext.Default.SplitRecordRoot));

        static void AssertRecord(SplitRecordRoot? result)
        {
            Assert.NotNull(result);
            Assert.Equal(1, result.A.Y);
            Assert.Equal(3, result.A.Child.Value);
            Assert.Equal(2, result.B.Value);
        }
    }

    private static void AssertCargoManifest(CargoManifest? result)
    {
        Assert.NotNull(result);
        Assert.Equal("demo", result.Package?.Name);
        Assert.Equal("1", result.Dependencies?["serde"]);
        Assert.NotNull(result.Bin);
        Assert.HasCount(2, result.Bin);
        Assert.Equal("first", result.Bin[0].Name);
        Assert.Null(result.Bin[0].Settings);
        Assert.Equal("second", result.Bin[1].Name);
        Assert.Equal("3", result.Bin[1].Settings?.Opt);
        Assert.NotNull(result.Example);
        Assert.Equal(["ex1", "ex2"], result.Example.Select(e => e.Name));
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
