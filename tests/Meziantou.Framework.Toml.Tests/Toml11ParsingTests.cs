using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Tests;

public sealed class Toml11ParsingTests
{
    [Fact]
    public void InlineTable_MultilineWithCommentsAndTrailingComma_IsAccepted()
    {
        var toml =
            """
            a = {
              b = 1, # comment
              c = 2,
            }
            """;

        var result = TomlSerializer.Deserialize<object>(toml);
        Assert.IsType<TomlTable>(result);

        var table = (TomlTable)result!;
        Assert.IsType<TomlTable>(table["a"]);

        var inline = (TomlTable)table["a"];
        Assert.Equal(1L, inline["b"]);
        Assert.Equal(2L, inline["c"]);
    }

    [Theory]
    [InlineData("a = {b =\n1}\n")]
    [InlineData("a = {b = # comment\n1}\n")]
    [InlineData("a = {b = 1, c =\r\n2}\n")]
    [InlineData("a = {b\n= 1}\n")]
    public void InlineTable_NewlineInsideKeyValuePair_IsRejected(string toml)
    {
        Assert.True(SyntaxParser.Parse(toml).HasErrors);
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));

        var parser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        while (parser.MoveNext())
        {
        }

        Assert.True(parser.HasErrors);
    }

    [Theory]
    [InlineData("\uFEFFa = 1\n", true)]
    [InlineData("\uFEFF", true)]
    [InlineData("\uFEFF# comment\n[t]\n", true)]
    [InlineData("a = 1\n", false)]
    public void SyntaxParser_ByteOrderMark_IsKept(string toml, bool hasByteOrderMark)
    {
        var doc = SyntaxParser.Parse(toml);

        Assert.False(doc.HasErrors);
        Assert.Equal(hasByteOrderMark, doc.HasByteOrderMark);
        Assert.Equal(toml, doc.ToString());
        Assert.Equal(toml, SyntaxParser.Parse(new StringReader(toml)).ToString());
    }

    [Theory]
    [InlineData("[a\nb=1")]
    [InlineData("[a\r\nb=1\r\n")]
    [InlineData("a = \nb = 2\n")]
    [InlineData("a b c = 1\n")]
    [InlineData("= 1\n[t]]\n")]
    [InlineData("a = [1 2]\n")]
    [InlineData("a = {b = 1 c = 2}\n")]
    [InlineData("a = 1 2 3\nb = \"x")]
    [InlineData("[[t]\n")]
    [InlineData("a.=1\n")]
    public void SyntaxParser_InvalidDocument_KeepsEveryCharacter(string toml)
    {
        var doc = SyntaxParser.Parse(toml);

        Assert.True(doc.HasErrors);
        Assert.Equal(toml, doc.ToString());
    }

    [Fact]
    public void LocalTime_MinuteOnly_IsAccepted()
    {
        var reader = TomlReader.Create("t = 07:32\n");
        Assert.True(reader.Read()); // StartDocument
        Assert.True(reader.Read()); // StartTable
        Assert.True(reader.Read()); // PropertyName
        Assert.Equal("t", reader.PropertyName);
        Assert.True(reader.Read()); // DateTime
        Assert.Equal(TomlTokenType.DateTime, reader.TokenType);
        var value = reader.GetTomlDateTime();
        Assert.Equal(TomlDateTimeKind.LocalTime, value.Kind);
    }

    [Fact]
    public void LocalDateTime_MinuteOnly_IsAccepted()
    {
        var reader = TomlReader.Create("dt = 1979-05-27T07:32\n");
        Assert.True(reader.Read()); // StartDocument
        Assert.True(reader.Read()); // StartTable
        Assert.True(reader.Read()); // PropertyName
        Assert.Equal("dt", reader.PropertyName);
        Assert.True(reader.Read()); // DateTime
        var value = reader.GetTomlDateTime();
        Assert.Equal(TomlDateTimeKind.LocalDateTime, value.Kind);
    }

    [Fact]
    public void BasicString_ByteAndEscapeEscapes_AreDecoded()
    {
        var reader = TomlReader.Create("s = \"A\\e\\x41\"\n");
        reader.Read(); // StartDocument
        reader.Read(); // StartTable
        reader.Read(); // PropertyName
        reader.Read(); // String
        Assert.Equal("A\u001BA", reader.GetString());
    }

    [Fact]
    public void BasicString_InvalidByteEscape_ThrowsWithLocation()
    {
        var options = new TomlSerializerOptions { RootValueHandling = TomlRootValueHandling.WrapInRootKey };
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<string>("value = \"\\x0\"\n", options));
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);
        Assert.Contains("(1,", ex.Message);
    }

    [Fact]
    public void TomlLexer_Default_DoesNotDecode_ButStillLexes()
    {
        var lexer = TomlLexer.Create("s = \"A\\e\\x41\"\n");
        while (lexer.MoveNext())
        {
            if (lexer.Current.Kind == TokenKind.String)
            {
                Assert.Null(lexer.Current.StringValue);
                return;
            }
        }

        Assert.Fail("Expected a string token.");
    }

    [Fact]
    public void TomlParser_DecodeScalars_ProducesMaterializedStringValue()
    {
        var parser = TomlParser.Create(
            "s = \"A\\e\\x41\"\n",
            new Meziantou.Framework.Toml.Parsing.TomlParserOptions { DecodeScalars = true });

        while (parser.MoveNext())
        {
            if (parser.Current.Kind == TomlParseEventKind.String)
            {
                Assert.Equal("A\u001BA", parser.Current.StringValue);
                Assert.Equal("A\u001BA", parser.GetString());
                return;
            }
        }

        Assert.Fail("Expected a string parse event.");
    }
}
