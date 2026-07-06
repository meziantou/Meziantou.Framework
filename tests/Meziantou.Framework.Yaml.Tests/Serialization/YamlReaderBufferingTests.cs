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
}
