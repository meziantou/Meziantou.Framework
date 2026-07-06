using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

/// <summary>
/// Tests for C# <c>required</c> keyword support in both reflection and source-generated modes.
/// Verifies that types with <c>required</c> members compile, deserialize, serialize, and validate correctly.
/// </summary>
public sealed class CSharpRequiredKeywordTests
{
    // ─── Reflection mode tests ──────────────────────────────────────

    [Fact]
    public void Reflection_RequiredSetProperty_DeserializesCorrectly()
    {
        var result = YamlSerializer.Deserialize<CSharpRequiredSetModel>("Name: Alice\nValue: 42\n")!;
        Assert.Equal("Alice", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reflection_RequiredSetProperty_SerializesCorrectly()
    {
        var yaml = YamlSerializer.Serialize(new CSharpRequiredSetModel { Name = "Bob", Value = 7 });
        Assert.Contains("Name: Bob", yaml);
        Assert.Contains("Value: 7", yaml);
    }

    [Fact]
    public void Reflection_RequiredSetProperty_MissingRequired_Throws()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<CSharpRequiredSetModel>("Value: 42\n"));
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Reflection_RequiredInitProperty_DeserializesCorrectly()
    {
        var result = YamlSerializer.Deserialize<CSharpRequiredInitModel>("Id: hello\nScore: 99\n")!;
        Assert.Equal("hello", result.Id);
        Assert.Equal(99, result.Score);
    }

    [Fact]
    public void Reflection_MixedRequiredModel_DeserializesCorrectly()
    {
        var result = YamlSerializer.Deserialize<CSharpMixedRequiredModel>("Name: Ada\nLabel: test\nId: abc\nOptional: opt\n")!;
        Assert.Equal("Ada", result.Name);
        Assert.Equal("test", result.Label);
        Assert.Equal("abc", result.Id);
        Assert.Equal("opt", result.Optional);
    }

    [Fact]
    public void Reflection_MixedRequiredModel_MissingCSharpRequired_Throws()
    {
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<CSharpMixedRequiredModel>("Label: test\nId: abc\n"));
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Reflection_MixedRequiredModel_MissingAttributeRequired_Throws()
    {
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<CSharpMixedRequiredModel>("Name: Ada\nId: abc\n"));
        Assert.Contains("Label", ex.Message);
    }

    [Fact]
    public void Reflection_RequiredWithOptional_OptionalCanBeOmitted()
    {
        var result = YamlSerializer.Deserialize<CSharpRequiredSetModel>("Name: Alice\nValue: 1\n")!;
        Assert.Equal("Alice", result.Name);
        Assert.Equal(1, result.Value);
        Assert.Null(result.Optional);
    }

    [Fact]
    public void Reflection_RequiredRecord_DeserializesCorrectly()
    {
        var result = YamlSerializer.Deserialize<CSharpRequiredRecord>("Host: db-server\nPort: 5432\n")!;
        Assert.Equal("db-server", result.Host);
        Assert.Equal(5432, result.Port);
    }

    [Fact]
    public void Reflection_RequiredRecord_MissingRequired_Throws()
    {
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<CSharpRequiredRecord>("Port: 5432\n"));
        Assert.Contains("Host", ex.Message);
    }

    [Fact]
    public void Reflection_RequiredValueType_DeserializesCorrectly()
    {
        var result = YamlSerializer.Deserialize<CSharpRequiredValueTypeModel>("Count: 10\nFlag: true\n")!;
        Assert.Equal(10, result.Count);
        Assert.Equal(true, result.Flag);
    }

    [Fact]
    public void Reflection_RequiredValueType_MissingRequired_Throws()
    {
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<CSharpRequiredValueTypeModel>("Flag: true\n"));
        Assert.Contains("Count", ex.Message);
    }

    // ─── Source generation mode tests ───────────────────────────────

    [Fact]
    public void SourceGen_RequiredSetProperty_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Name: Alice\nValue: 42\n", context.CSharpRequiredSetModel)!;
        Assert.Equal("Alice", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void SourceGen_RequiredSetProperty_SerializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var yaml = YamlSerializer.Serialize(new CSharpRequiredSetModel { Name = "Bob", Value = 7 }, context.CSharpRequiredSetModel);
        Assert.Contains("Name: Bob", yaml);
        Assert.Contains("Value: 7", yaml);
    }

    [Fact]
    public void SourceGen_RequiredSetProperty_MissingRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Value: 42\n", context.CSharpRequiredSetModel));
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void SourceGen_RequiredInitProperty_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Id: hello\nScore: 99\n", context.CSharpRequiredInitModel)!;
        Assert.Equal("hello", result.Id);
        Assert.Equal(99, result.Score);
    }

    [Fact]
    public void SourceGen_RequiredInitProperty_MissingRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Score: 99\n", context.CSharpRequiredInitModel));
        Assert.Contains("Id", ex.Message);
    }

    [Fact]
    public void SourceGen_MixedRequiredModel_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Name: Ada\nLabel: test\nId: abc\nOptional: opt\n", context.CSharpMixedRequiredModel)!;
        Assert.Equal("Ada", result.Name);
        Assert.Equal("test", result.Label);
        Assert.Equal("abc", result.Id);
        Assert.Equal("opt", result.Optional);
    }

    [Fact]
    public void SourceGen_MixedRequiredModel_MissingCSharpRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Label: test\nId: abc\n", context.CSharpMixedRequiredModel));
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void SourceGen_MixedRequiredModel_MissingAttributeRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Name: Ada\nId: abc\n", context.CSharpMixedRequiredModel));
        Assert.Contains("Label", ex.Message);
    }

    [Fact]
    public void SourceGen_MixedRequiredModel_MissingMultiple_ThrowsWithAllNames()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Id: abc\n", context.CSharpMixedRequiredModel));
        Assert.Contains("Name", ex.Message);
        Assert.Contains("Label", ex.Message);
    }

    [Fact]
    public void SourceGen_RequiredWithOptional_OptionalCanBeOmitted()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Name: Alice\nValue: 1\n", context.CSharpRequiredSetModel)!;
        Assert.Equal("Alice", result.Name);
        Assert.Equal(1, result.Value);
        Assert.Null(result.Optional);
    }

    [Fact]
    public void SourceGen_RequiredRecord_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Host: db-server\nPort: 5432\n", context.CSharpRequiredRecord)!;
        Assert.Equal("db-server", result.Host);
        Assert.Equal(5432, result.Port);
    }

    [Fact]
    public void SourceGen_RequiredRecord_SerializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var yaml = YamlSerializer.Serialize(new CSharpRequiredRecord { Host = "localhost", Port = 80 }, context.CSharpRequiredRecord);
        Assert.Contains("Host: localhost", yaml);
        Assert.Contains("Port: 80", yaml);
    }

    [Fact]
    public void SourceGen_RequiredRecord_MissingRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Port: 5432\n", context.CSharpRequiredRecord));
        Assert.Contains("Host", ex.Message);
    }

    [Fact]
    public void SourceGen_RequiredValueType_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Count: 10\nFlag: true\n", context.CSharpRequiredValueTypeModel)!;
        Assert.Equal(10, result.Count);
        Assert.Equal(true, result.Flag);
    }

    [Fact]
    public void SourceGen_RequiredValueType_MissingRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Flag: true\n", context.CSharpRequiredValueTypeModel));
        Assert.Contains("Count", ex.Message);
    }

    [Fact]
    public void SourceGen_RequiredWithNamingPolicy_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordCamelCaseContext.Default;
        var result = YamlSerializer.Deserialize("firstName: Ada\nlastName: Lovelace\n", context.CSharpRequiredNamingPolicyModel)!;
        Assert.Equal("Ada", result.FirstName);
        Assert.Equal("Lovelace", result.LastName);
    }

    [Fact]
    public void SourceGen_RequiredWithNamingPolicy_MissingRequired_ThrowsWithYamlName()
    {
        var context = CSharpRequiredKeywordCamelCaseContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("lastName: Lovelace\n", context.CSharpRequiredNamingPolicyModel));
        Assert.Contains("firstName", ex.Message);
    }

    [Fact]
    public void SourceGen_RequiredWithCustomOptions_DeserializesCorrectly()
    {
        var context = new CSharpRequiredKeywordTestContext(new YamlSerializerOptions { PropertyNameCaseInsensitive = true });
        var result = YamlSerializer.Deserialize("name: Alice\nvalue: 42\n", context.CSharpRequiredSetModel)!;
        Assert.Equal("Alice", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void SourceGen_RequiredInheritance_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Host: db-server\nPort: 5432\nTimeout: 30\n", context.CSharpRequiredDerivedModel)!;
        Assert.Equal("db-server", result.Host);
        Assert.Equal(5432, result.Port);
        Assert.Equal(30, result.Timeout);
    }

    [Fact]
    public void SourceGen_RequiredInheritance_MissingBaseRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Port: 5432\nTimeout: 30\n", context.CSharpRequiredDerivedModel));
        Assert.Contains("Host", ex.Message);
    }

    [Fact]
    public void SourceGen_RequiredInheritance_MissingDerivedRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("Host: db-server\nTimeout: 30\n", context.CSharpRequiredDerivedModel));
        Assert.Contains("Port", ex.Message);
    }

    [Fact]
    public void SourceGen_RequiredOnlyModel_DeserializesCorrectly()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var result = YamlSerializer.Deserialize("Tag: test\n", context.CSharpRequiredOnlyModel)!;
        Assert.Equal("test", result.Tag);
    }

    [Fact]
    public void SourceGen_RequiredOnlyModel_MissingRequired_Throws()
    {
        var context = CSharpRequiredKeywordTestContext.Default;
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize("{}\n", context.CSharpRequiredOnlyModel));
        Assert.Contains("Tag", ex.Message);
    }
}

// ─── Model types (at namespace level for source generator) ──────
#pragma warning disable MA0048 // File name must match type name

public sealed class CSharpRequiredSetModel
{
    public required string Name { get; set; }
    public required int Value { get; set; }
    public string? Optional { get; set; }
}

public sealed class CSharpRequiredInitModel
{
    public required string Id { get; init; }
    public int Score { get; set; }
}

public sealed class CSharpMixedRequiredModel
{
    public required string Name { get; set; }

    [YamlRequired]
    public string Label { get; set; } = "";

    public required string Id { get; init; }

    public string? Optional { get; set; }
}

public record CSharpRequiredRecord
{
    public required string Host { get; set; }
    public required int Port { get; set; }
}

public sealed class CSharpRequiredValueTypeModel
{
    public required int Count { get; set; }
    public required bool Flag { get; set; }
}

public sealed class CSharpRequiredNamingPolicyModel
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
}

public class CSharpRequiredBaseModel
{
    public required string Host { get; set; }
}

public sealed class CSharpRequiredDerivedModel : CSharpRequiredBaseModel
{
    public required int Port { get; set; }
    public int Timeout { get; set; }
}

public sealed class CSharpRequiredOnlyModel
{
    public required string Tag { get; set; }
}

// ─── Source generation contexts ─────────────────────────────────

[YamlSerializable(typeof(CSharpRequiredSetModel))]
[YamlSerializable(typeof(CSharpRequiredInitModel))]
[YamlSerializable(typeof(CSharpMixedRequiredModel))]
[YamlSerializable(typeof(CSharpRequiredRecord))]
[YamlSerializable(typeof(CSharpRequiredValueTypeModel))]
[YamlSerializable(typeof(CSharpRequiredDerivedModel))]
[YamlSerializable(typeof(CSharpRequiredOnlyModel))]
internal sealed partial class CSharpRequiredKeywordTestContext : YamlSerializerContext
{
    public CSharpRequiredKeywordTestContext()
    {
    }

    public CSharpRequiredKeywordTestContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

[YamlSourceGenerationOptions(PropertyNamingPolicy = YamlKnownNamingPolicy.CamelCase)]
[YamlSerializable(typeof(CSharpRequiredNamingPolicyModel))]
internal sealed partial class CSharpRequiredKeywordCamelCaseContext : YamlSerializerContext
{
}
