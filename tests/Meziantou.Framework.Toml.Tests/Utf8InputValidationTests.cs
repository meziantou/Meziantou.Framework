using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name

public sealed class BomInputTests
{
    [Fact]
    public void TomlReader_StringWithBom_IsAccepted()
    {
        var reader = TomlReader.Create("\uFEFFa = 1\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal("a", reader.PropertyName);
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64());
    }

    [Theory]
    [InlineData("a = \"x\uFFFDy\"\n")]
    [InlineData("a = 'x\uFFFDy'\n")]
    [InlineData("a = \"\"\"x\\u0041\uFFFDy\"\"\"\n")]
    [InlineData("# comment \uFFFD\na = \"x\uFFFDy\"\n")]
    public void ReplacementCharacter_IsValid(string toml)
    {
        Assert.False(Parsing.SyntaxParser.Parse(toml).HasErrors);
        var value = (string)TomlSerializer.Deserialize<Model.TomlTable>(toml)!["a"];
        Assert.Contains('\uFFFD', value);
    }

    // xunit serializes the data of the test cases as UTF-8, which replaces a lone surrogate, so the text is built here
    [Theory]
    [InlineData(0xD800, "y\"\n")]
    [InlineData(0xDC00, "y\"\n")]
    [InlineData(0xD800, "")]
    public void UnpairedSurrogate_IsInvalid(int surrogate, string suffix)
    {
        var toml = "a = \"x" + (char)surrogate + suffix;
        Assert.True(Parsing.SyntaxParser.Parse(toml).HasErrors);
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(toml));
    }

    [Theory]
    [InlineData("a = \"x", "\"\n")]
    [InlineData("a = \"\"\"x", " y\"\"\"\n")]
    [InlineData("# x", "\na = 1\n")]
    public void UnpairedHighSurrogate_DoesNotHideTheNextCharacter(string prefix, string suffix)
    {
        var toml = prefix + (char)0xD800 + suffix;

        var doc = Parsing.SyntaxParser.Parse(toml);

        var diagnostic = Assert.Single(doc.Diagnostics);
        Assert.Contains("surrogate", diagnostic.Message, StringComparison.Ordinal);
    }
}
