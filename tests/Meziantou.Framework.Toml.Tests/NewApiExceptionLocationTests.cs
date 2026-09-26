using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public class NewApiExceptionLocationTests
{
    [Fact]
    public void Deserialize_InvalidScalarType_IncludesLocation()
    {
        var options = new TomlSerializerOptions
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
            SourceName = "test.toml",
        };

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<long>("value = \"abc\"\n", options));
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.Equal(1, ex.Line);
        Assert.Equal(9, ex.Column);
        Assert.Contains("test.toml(1,9)", ex.Message);
    }

    [Fact]
    public void Parse_SyntaxError_IncludesLocation()
    {
        var options = new TomlSerializerOptions { SourceName = "test.toml" };

        var reader = TomlReader.Create("a =\n", options);
        reader.Read(); // StartDocument
        reader.Read(); // StartTable
        reader.Read(); // PropertyName
        var ex = Assert.Throws<TomlException>(() => reader.Read()); // missing value
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);
        Assert.Contains("test.toml(", ex.Message);
    }

    [Fact]
    public void Parse_InvalidKeyHexEscape_IncludesLocation()
    {
        var options = new TomlSerializerOptions { SourceName = "keys.toml" };
        var reader = TomlReader.Create("\"a\\xG0\" = 1\n", options);

        var ex = Assert.Throws<TomlException>(() =>
        {
            while (reader.Read())
            {
            }
        });

        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);
        Assert.Contains("keys.toml(", ex.Message);
    }

    [Fact]
    public void Parse_InvalidDateTimeLiteral_IncludesLocation()
    {
        var options = new TomlSerializerOptions { SourceName = "dt.toml" };
        var reader = TomlReader.Create("dt = 1979-05-27T07:32:00+24:00\n", options);

        reader.Read(); // StartDocument
        reader.Read(); // StartTable
        reader.Read(); // PropertyName

        var ex = Assert.Throws<TomlException>(() => reader.Read());
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);
        Assert.Contains("dt.toml(", ex.Message);
    }

    [Fact]
    public void Parse_InvalidUnicodeEscapeInKey_IncludesLocation()
    {
        var options = new TomlSerializerOptions { SourceName = "key.toml" };
        var reader = TomlReader.Create("\"a\\uD800\" = 1\n", options);

        var ex = Assert.Throws<TomlException>(() => reader.Read()); // StartDocument (lexer detects invalid escape)
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);
        Assert.Contains("key.toml(", ex.Message);
    }

    [Fact]
    public void Deserialize_RootValueKeyNotFound_IncludesLocation()
    {
        var options = new TomlSerializerOptions
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
            RootValueKeyName = "value",
            SourceName = "root.toml",
        };

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<long>("other = 1\n", options));
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);
        Assert.Contains("root.toml(", ex.Message);
    }

    public sealed class MissingRequiredModel
    {
        [JsonRequired]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void Deserialize_ReflectionMissingRequiredKey_IncludesLocation()
    {
        var options = new TomlSerializerOptions { SourceName = "required.toml" };

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<MissingRequiredModel>("other = 1\n", options));
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.True(ex.Line > 0);
        Assert.True(ex.Column > 0);
        Assert.Contains("required.toml(", ex.Message);
    }
}
