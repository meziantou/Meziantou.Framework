using System.Collections.Generic;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name

public sealed class InlineTableOverrideHolder
{
    [TomlInlineTable(TomlInlineTablePolicy.Always)]
    public InlineTableOverrideChild Child { get; set; } = new() { X = 1 };

    public InlineTableOverrideChild Other { get; set; } = new() { X = 2 };
}

public sealed class InlineTableOverrideChild
{
    public int X { get; set; }
}

[TomlMappingOrder(TomlMappingOrderPolicy.Alphabetical)]
public sealed class AttributeOrderedHolder
{
    public int Z { get; set; } = 2;

    public int A { get; set; } = 1;
}

[TomlDottedKeyHandling(TomlDottedKeyHandling.Expand)]
public sealed class AttributeDottedKeyHolder
{
    [TomlPropertyName("a.b")]
    public int Value { get; set; } = 1;

    public Dictionary<string, int> Map { get; set; } = new() { ["x.y"] = 2 };
}

public sealed class AttributeStringStyleHolder
{
    [TomlStringStyle(TomlStringStyle.Basic)]
    public string FallsBackToGlobalPreferLiteral { get; set; } = "safe";

    [TomlStringStyle(TomlStringStyle.Basic, PreferLiteralWhenNoEscapes = TomlBooleanPreference.True)]
    public string OverridesPreferLiteral { get; set; } = "safe";
}

public sealed class MultilineStringStyleHolder
{
    [TomlStringStyle(TomlStringStyle.MultilineBasic)]
    public string Basic { get; set; } = "";

    [TomlStringStyle(TomlStringStyle.MultilineLiteral)]
    public string Literal { get; set; } = "";
}

public sealed class HexEscapeStringStyleHolder
{
    [TomlStringStyle(TomlStringStyle.Basic, AllowHexEscapes = TomlBooleanPreference.True)]
    public string Hex { get; set; } = "\u0001\u001B\u007F";

    public string Default { get; set; } = "\u0001\u001B\u007F";
}

public sealed class InlineStyledChildHolder
{
    [TomlInlineTable(TomlInlineTablePolicy.Always)]
    public StyledChild Child { get; set; } = new();
}

public sealed class StyledChild
{
    [TomlStringStyle(TomlStringStyle.Literal)]
    public string Path { get; set; } = "C:\\temp";

    [TomlStringStyle(TomlStringStyle.MultilineBasic)]
    public string Text { get; set; } = "a\nb";
}

[TomlDottedKeyHandling(TomlDottedKeyHandling.Expand)]
public sealed class ExpandedStyledHolder
{
    [TomlPropertyName("a.s")]
    [TomlStringStyle(TomlStringStyle.Literal)]
    public string S { get; set; } = "x";
}

[TomlPolymorphic]
[TomlDerivedType(typeof(ExpandedStyledDerived), "d")]
public class ExpandedStyledBase
{
}

public sealed class ExpandedStyledDerived : ExpandedStyledBase
{
    [TomlStringStyle(TomlStringStyle.MultilineBasic)]
    public string Name { get; set; } = "n";
}

// The dotted member and the member named after its first segment write the same table
[TomlDottedKeyHandling(TomlDottedKeyHandling.Expand)]
public sealed class ExpandedStyledSharedTableHolder
{
    [TomlPropertyName("p.x")]
    [TomlStringStyle(TomlStringStyle.Literal)]
    public string X { get; set; } = "x";

    [TomlPropertyName("p")]
    public ExpandedStyledBase P { get; set; } = new ExpandedStyledDerived();
}

public sealed class InvalidStringStyleAttributeHolder
{
    [TomlStringStyle(TomlStringStyle.Basic)]
    public int NotString { get; set; } = 1;
}

[TomlSerializable(typeof(InlineTableOverrideHolder))]
[TomlSerializable(typeof(AttributeOrderedHolder))]
[TomlSerializable(typeof(AttributeDottedKeyHolder))]
[TomlSerializable(typeof(AttributeStringStyleHolder))]
[TomlSerializable(typeof(MultilineStringStyleHolder))]
[TomlSerializable(typeof(HexEscapeStringStyleHolder))]
[TomlSerializable(typeof(InlineStyledChildHolder))]
[TomlSerializable(typeof(ExpandedStyledHolder))]
[TomlSerializable(typeof(ExpandedStyledSharedTableHolder))]
internal sealed partial class TestTomlStyleAttributesContext : TomlSerializerContext
{
}

public class NewApiStyleAttributeTests
{
    [Fact]
    public void StringStyleAllowHexEscapes_UsesTheToml11Escapes()
    {
        var value = new HexEscapeStringStyleHolder();
        var expected = "Hex = \"\\x01\\e\\x7F\"\nDefault = \"\\u0001\\u001B\\u007F\"\n";

        var reflectionToml = TomlSerializer.Serialize(value);
        var generatedToml = TomlSerializer.Serialize(value, TestTomlStyleAttributesContext.Default.HexEscapeStringStyleHolder);

        Assert.Equal(expected, reflectionToml.ReplaceLineEndings("\n"));
        Assert.Equal(expected, generatedToml.ReplaceLineEndings("\n"));
        var roundtrip = TomlSerializer.Deserialize<HexEscapeStringStyleHolder>(reflectionToml)!;
        Assert.Equal(value.Hex, roundtrip.Hex);
        Assert.Equal(value.Default, roundtrip.Default);
    }

    [Theory]
    [InlineData(false, "\"k\\u0001\" = \"v\\u0080\"\n")]
    [InlineData(true, "\"k\\x01\" = \"v\\x80\"\n")]
    public void StringStylePreferencesAllowHexEscapes_AppliesToKeysAndValues(bool allowHexEscapes, string expected)
    {
        var options = TomlSerializerOptions.Default with { StringStylePreferences = new TomlStringStylePreferences { AllowHexEscapes = allowHexEscapes } };
        var value = new Dictionary<string, string> { ["k\u0001"] = "v\u0080" };

        var toml = TomlSerializer.Serialize(value, options);

        Assert.Equal(expected, toml.ReplaceLineEndings("\n"));
        Assert.Equal(value, TomlSerializer.Deserialize<Dictionary<string, string>>(toml));
    }

    [Fact]
    public void PropertyInlineTableOverride_AppliesOnlyToAnnotatedProperty()
    {
        var value = new InlineTableOverrideHolder();

        var reflectionToml = TomlSerializer.Serialize(value);
        var generatedToml = TomlSerializer.Serialize(value, TestTomlStyleAttributesContext.Default.InlineTableOverrideHolder);

        Assert.Contains("Child = {X = 1}", reflectionToml);
        Assert.Contains("[Other]", reflectionToml);
        Assert.Contains("Child = {X = 1}", generatedToml);
        Assert.Contains("[Other]", generatedToml);
    }

    [Fact]
    public void MemberStyles_ApplyInsideInlineTables()
    {
        var expected = "Child = {Path = 'C:\\temp', Text = \"\"\"\na\nb\"\"\"}\n";

        var reflectionToml = TomlSerializer.Serialize(new InlineStyledChildHolder());
        var generatedToml = TomlSerializer.Serialize(new InlineStyledChildHolder(), TestTomlStyleAttributesContext.Default.InlineStyledChildHolder);

        Assert.Equal(expected, reflectionToml.ReplaceLineEndings("\n"));
        Assert.Equal(expected, generatedToml.ReplaceLineEndings("\n"));
        Assert.Equal("a\nb", TomlSerializer.Deserialize<InlineStyledChildHolder>(reflectionToml)!.Child.Text);
    }

    [Fact]
    public void MemberStyles_ApplyToMembersWithAnExpandedDottedName()
    {
        const string Expected = "[a]\ns = 'x'\n";

        Assert.Equal(Expected, TomlSerializer.Serialize(new ExpandedStyledHolder()).ReplaceLineEndings("\n"));
        Assert.Equal(Expected, TomlSerializer.Serialize(new ExpandedStyledHolder(), TestTomlStyleAttributesContext.Default.ExpandedStyledHolder).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void MemberStyles_OfAnExpandedDottedName_AreKeptWhenAnotherMemberWritesTheSameTable()
    {
        var reflection = TomlSerializer.Serialize(new ExpandedStyledSharedTableHolder());
        var generated = TomlSerializer.Serialize(new ExpandedStyledSharedTableHolder(), TestTomlStyleAttributesContext.Default.ExpandedStyledSharedTableHolder);

        Assert.Contains("x = 'x'", reflection, System.StringComparison.Ordinal);
        Assert.Contains("x = 'x'", generated, System.StringComparison.Ordinal);
        Assert.Contains("Name = \"\"\"", reflection, System.StringComparison.Ordinal);
        Assert.Contains("Name = \"\"\"", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TypeMappingOrderOverride_OrdersMembersAlphabetically()
    {
        var reflectionToml = TomlSerializer.Serialize(new AttributeOrderedHolder());
        var generatedToml = TomlSerializer.Serialize(new AttributeOrderedHolder(), TestTomlStyleAttributesContext.Default.AttributeOrderedHolder);

        Assert.True(reflectionToml.IndexOf("A = 1", System.StringComparison.Ordinal) < reflectionToml.IndexOf("Z = 2", System.StringComparison.Ordinal));
        Assert.True(generatedToml.IndexOf("A = 1", System.StringComparison.Ordinal) < generatedToml.IndexOf("Z = 2", System.StringComparison.Ordinal));
    }

    [Fact]
    public void TypeDottedKeyOverride_ExpandsMemberNamesButNotDictionaryKeys()
    {
        var reflectionToml = TomlSerializer.Serialize(new AttributeDottedKeyHolder());
        var generatedToml = TomlSerializer.Serialize(new AttributeDottedKeyHolder(), TestTomlStyleAttributesContext.Default.AttributeDottedKeyHolder);

        Assert.Contains("[a]", reflectionToml);
        Assert.Contains("b = 1", reflectionToml);
        Assert.Contains("\"x.y\" = 2", reflectionToml);
        Assert.DoesNotContain("[Map.x]", reflectionToml);
        Assert.Contains("[a]", generatedToml);
        Assert.Contains("b = 1", generatedToml);
        Assert.Contains("\"x.y\" = 2", generatedToml);
        Assert.DoesNotContain("[Map.x]", generatedToml);
    }

    [Fact]
    public void PropertyStringStyle_UnspecifiedPreferencesFallbackToGlobalOptions()
    {
        var options = new TomlSerializerOptions
        {
            StringStylePreferences = new TomlStringStylePreferences
            {
                PreferLiteralWhenNoEscapes = false,
            },
        };

        var toml = TomlSerializer.Serialize(new AttributeStringStyleHolder(), options);

        Assert.Contains("FallsBackToGlobalPreferLiteral = \"safe\"", toml);
        Assert.Contains("OverridesPreferLiteral = 'safe'", toml);
    }

    [Fact]
    public void Reflection_StringStyleAttributeRejectsNonStringMembers()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new InvalidStringStyleAttributeHolder()));
    }

    [Theory]
    [InlineData("\nabc")]
    [InlineData("\n")]
    [InlineData("\r\nabc\r\n")]
    [InlineData("abc")]
    [InlineData("")]
    public void MultilineStringStyle_KeepsLeadingNewLine(string value)
    {
        var model = new MultilineStringStyleHolder { Basic = value, Literal = value };

        foreach (var toml in new[] { TomlSerializer.Serialize(model), TomlSerializer.Serialize(model, TestTomlStyleAttributesContext.Default.MultilineStringStyleHolder) })
        {
            Assert.Contains("Basic = \"\"\"\n", toml, StringComparison.Ordinal);
            var roundtrip = TomlSerializer.Deserialize<MultilineStringStyleHolder>(toml)!;
            Assert.Equal(value, roundtrip.Basic);
            Assert.Equal(value, roundtrip.Literal);
        }
    }
}
