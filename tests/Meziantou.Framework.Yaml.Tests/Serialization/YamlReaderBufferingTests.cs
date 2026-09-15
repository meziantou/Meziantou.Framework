using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlReaderBufferingTests
{
    [Fact]
    public void BufferCurrentNodeToStringAndFindDiscriminator_ExtractsValue_AndAdvancesReader()
    {
        var yaml = "- $type: dog\n  Name: Rex\n- $type: cat\n  Name: Mittens\n";
        var options = new YamlSerializerOptions { PropertyNameCaseInsensitive = false };

        var reader = YamlReader.Create(yaml, options);
        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartSequence, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartMapping, reader.TokenType);

        var buffered = YamlReader.BufferCurrentNodeToStringAndFindDiscriminator(reader, "$type", out var discriminator);
        Assert.Equal("dog", discriminator);
        Assert.Contains("$type: dog", buffered);
        Assert.Contains("Name: Rex", buffered);

        // Reader should be positioned at the next sequence item (second mapping).
        Assert.Equal(YamlTokenType.StartMapping, reader.TokenType);
        var buffered2 = YamlReader.BufferCurrentNodeToStringAndFindDiscriminator(reader, "$type", out var discriminator2);
        Assert.Equal("cat", discriminator2);
        Assert.Contains("$type: cat", buffered2);
        Assert.Contains("Name: Mittens", buffered2);

        Assert.Equal(YamlTokenType.EndSequence, reader.TokenType);
    }

    [Fact]
    public void BufferCurrentNodeToStringAndFindDiscriminator_RespectsCaseInsensitiveOption()
    {
        var yaml = "- $TYPE: dog\n  Name: Rex\n";
        var options = new YamlSerializerOptions { PropertyNameCaseInsensitive = true };

        var reader = YamlReader.Create(yaml, options);
        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartSequence, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartMapping, reader.TokenType);

        _ = YamlReader.BufferCurrentNodeToStringAndFindDiscriminator(reader, "$type", out var discriminator);
        Assert.Equal("dog", discriminator);
    }

    [Theory]
    [InlineData("\"42\"\n")]
    [InlineData("'true'\n")]
    [InlineData("42\n")]
    [InlineData("---\n")]
    [InlineData("|\n  line1\n  line2\n")]
    [InlineData(">\n  folded\n  text\n")]
    [InlineData("|+\n  kept\n\n")]
    [InlineData("- \"1\"\n- '2'\n- 3\n-\n- |-\n  4\n- a, [b]\n")]
    [InlineData("plain: 1\ndouble: \"2\"\nsingle: 'it''s'\nempty:\nquotedEmpty: ''\nliteral: |\n  text\nfolded: >-\n  text\n")]
    [InlineData("<<: {a: 1}\n\"<<\": 2\n'<<': 3\n\"42\": plain key\n'x': single key\n")]
    [InlineData("tagged: !custom '42'\nanchored: &a \"x\"\nalias: *a\nemptyTagged: !custom\nemptyAnchored: &b\nlist: [a, 'b', \"c\"]\nmap: {k: 'v'}\n")]
    [InlineData("!!str 42\n")]
    [InlineData("value: !!int \"1\"\nempty: !!null\n")]
    [InlineData("!!map\nlist: !!seq\n- !!str 1\nmap: !!map {k: !!str v}\nflow: !!seq [a]\n")]
    [InlineData("!<tag:example.com,2024:x> value\n")]
    [InlineData("- !<tag:example.com,2024:a%21b%3E%20c> 1\n- !local%21tag x\n- !%E2%9C%93 y\n- ! 42\n- !<!verbatim-local> z\n- &a !!str b\n")]
    [InlineData("%TAG !e! tag:example.com,2024:app/\n---\n- !e!foo bar\n- !e!%5Bx%5D baz\n")]
    public void BufferCurrentNodeToString_PreservesScalarStyles(string yaml)
    {
        foreach (var writeIndented in new[] { true, false })
        {
            var options = new YamlSerializerOptions { WriteIndented = writeIndented };
            var reader = YamlReader.Create(yaml, options);
            Assert.True(reader.Read());

            var buffered = YamlReader.BufferCurrentNodeToString(reader);

            Assert.Equal(YamlTokenType.None, reader.TokenType);
            Assert.Equal(ReadTokens(yaml, options), ReadTokens(buffered, options));
        }
    }

    private static List<(YamlTokenType TokenType, string? Value, ScalarStyle Style, string? Tag, string? Anchor, string? Alias)> ReadTokens(string yaml, YamlSerializerOptions options)
    {
        var tokens = new List<(YamlTokenType, string?, ScalarStyle, string?, string?, string?)>();
        var reader = YamlReader.Create(yaml, options);
        while (reader.Read())
        {
            tokens.Add((reader.TokenType, reader.ScalarValue, reader.ScalarStyle, reader.Tag, reader.Anchor, reader.Alias));
        }

        return tokens;
    }
}
