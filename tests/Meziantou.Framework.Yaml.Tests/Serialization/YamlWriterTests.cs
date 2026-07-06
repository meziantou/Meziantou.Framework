using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlWriterTests
{
    [Fact]
    public void RootScalar_QuotesAndEscapesAsNeeded()
    {
        var cases = new (string Value, string ExpectedYaml)[]
        {
            ("plain", "plain"),
            ("", "''"),
            (" leading", "\" leading\""),
            ("trailing ", "\"trailing \""),
            ("a:b", "\"a:b\""),
            ("a#b", "\"a#b\""),
            ("a\nb", "\"a\\nb\""),
            ("\u0001", "\"\\u0001\""),
        };

        foreach (var @case in cases)
        {
            var writer = CreateWriter(new YamlSerializerOptions(), out var buffer);
            writer.WriteScalar(@case.Value);

            Assert.Equal(@case.ExpectedYaml, buffer.ToString());
        }
    }

    [Fact]
    public void PropertyName_WithDash_IsQuoted()
    {
        var writer = CreateWriter(new YamlSerializerOptions(), out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("-");
        writer.WriteScalar("x");
        writer.WriteEndMapping();

        Assert.Equal("\"-\": x", buffer.ToString());
    }

    [Fact]
    public void RootScalars_ForNumbersAndSpecialFloats_AreEmittedPlain()
    {
        var writer = CreateWriter(new YamlSerializerOptions(), out var buffer);

        writer.WriteStartSequence();
        writer.WriteScalar(42);
        writer.WriteScalar(1.5);
        writer.WriteScalar(double.PositiveInfinity);
        writer.WriteScalar(double.NegativeInfinity);
        writer.WriteScalar(double.NaN);
        writer.WriteEndSequence();

        Assert.Equal("- 42\n- 1.5\n- .inf\n- -.inf\n- .nan", buffer.ToString());
    }

    [Fact]
    public void Mapping_WithScalar_WritesOnSingleLine()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("a");
        writer.WriteScalar("1");
        writer.WriteEndMapping();

        Assert.Equal("a: 1", buffer.ToString());
    }

    [Fact]
    public void Mapping_WithNestedMapping_WritesIndentedBlock()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("parent");
        writer.WriteStartMapping();
        writer.WritePropertyName("child");
        writer.WriteScalar("x");
        writer.WriteEndMapping();
        writer.WriteEndMapping();

        Assert.Equal("parent:\n  child: x", buffer.ToString());
    }

    [Fact]
    public void Sequence_WithScalars_WritesDashLines()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartSequence();
        writer.WriteScalar("a");
        writer.WriteScalar("b");
        writer.WriteEndSequence();

        Assert.Equal("- a\n- b", buffer.ToString());
    }

    [Fact]
    public void Constructor_WithStringBuilder_WritesExpectedYaml()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var buffer = new System.Text.StringBuilder();
        var writer = new YamlWriter(buffer, options);

        writer.WriteStartMapping();
        writer.WritePropertyName("enabled");
        writer.WriteScalar(true);
        writer.WritePropertyName("port");
        writer.WriteScalar(5432);
        writer.WriteEndMapping();

        Assert.Equal("enabled: true\nport: 5432", buffer.ToString());
    }

    [Fact]
    public void CharacterScalar_WithSpecialCharacter_IsQuoted()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartSequence();
        writer.WriteScalar(':');
        writer.WriteEndSequence();

        Assert.Equal("- \":\"", buffer.ToString());
    }

    [Fact]
    public void EmptyContainers_AreWrittenInline()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("emptyMap");
        writer.WriteStartMapping();
        writer.WriteEndMapping();
        writer.WritePropertyName("emptySeq");
        writer.WriteStartSequence();
        writer.WriteEndSequence();
        writer.WriteEndMapping();

        Assert.Equal("emptyMap: {}\nemptySeq: []", buffer.ToString());
    }

    [Fact]
    public void SequenceItem_EmptyMapping_WritesInline()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartSequence();
        writer.WriteStartMapping();
        writer.WriteEndMapping();
        writer.WriteEndSequence();

        Assert.Equal("- {}", buffer.ToString());
    }

    private static YamlWriter CreateWriter(YamlSerializerOptions options, out StringWriter buffer)
    {
        buffer = new StringWriter();
        return new YamlWriter(buffer, options);
    }
}
