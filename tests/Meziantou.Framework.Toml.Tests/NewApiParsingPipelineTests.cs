using System.Collections.Generic;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Tests;

public class NewApiParsingPipelineTests
{
    [Fact]
    public void TomlParser_MoveNext_EmitsExpectedEvents()
    {
        var parser = TomlParser.Create(
            """
            name = "Ada"
            age = 37
            """);

        var events = new List<TomlParseEventKind>();
        var names = new List<string>();
        while (parser.MoveNext())
        {
            events.Add(parser.Current.Kind);
            if (parser.Current.Kind == TomlParseEventKind.PropertyName)
            {
                names.Add(parser.GetPropertyName());
            }
        }

        Assert.Equal(new[]
        {
            TomlParseEventKind.StartDocument,
            TomlParseEventKind.StartTable,
            TomlParseEventKind.PropertyName,
            TomlParseEventKind.String,
            TomlParseEventKind.PropertyName,
            TomlParseEventKind.Integer,
            TomlParseEventKind.EndTable,
            TomlParseEventKind.EndDocument,
        }, events);
        Assert.Equal(new[] { "name", "age" }, names);
    }

    [Fact]
    public void TomlLexer_ExposesTokensInKeyAndValueModes()
    {
        var lexer = TomlLexer.Create("age = 37\n");
        var tokens = new List<TokenKind>();

        while (lexer.MoveNext())
        {
            var token = lexer.Current;
            tokens.Add(token.Kind);

            if (token.Kind == TokenKind.Equal)
            {
                lexer.Mode = TomlLexerMode.Value;
            }
            else if (token.Kind == TokenKind.NewLine)
            {
                lexer.Mode = TomlLexerMode.Key;
            }
        }

        Assert.Contains(TokenKind.BasicKey, tokens);
        Assert.Contains(TokenKind.Equal, tokens);
        Assert.Contains(TokenKind.Integer, tokens);
    }

    [Fact]
    public void TomlLexer_Default_DoesNotDecodeStringValues()
    {
        var lexer = TomlLexer.Create("name = \"A\\e\\x41\"\n");
        while (lexer.MoveNext())
        {
            if (lexer.Current.Kind == TokenKind.Equal)
            {
                lexer.Mode = TomlLexerMode.Value;
            }

            if (lexer.Current.Kind == TokenKind.String)
            {
                Assert.Null(lexer.Current.StringValue);
                return;
            }
        }

        Assert.Fail("Expected to encounter a string token.");
    }

    [Fact]
    public void TomlLexer_DecodeScalars_EmitsDecodedStringValues()
    {
        var lexer = TomlLexer.Create("name = \"A\\e\\x41\"\n", new TomlLexerOptions { DecodeScalars = true });
        while (lexer.MoveNext())
        {
            if (lexer.Current.Kind == TokenKind.Equal)
            {
                lexer.Mode = TomlLexerMode.Value;
            }

            if (lexer.Current.Kind == TokenKind.String)
            {
                Assert.Equal("A\u001BA", lexer.Current.StringValue);
                return;
            }
        }

        Assert.Fail("Expected to encounter a string token.");
    }

    [Fact]
    public void TomlParser_TolerantMode_RecordsDiagnosticsAndTerminates()
    {
        var parser = TomlParser.Create(
            "a =\n",
            new Meziantou.Framework.Toml.Parsing.TomlParserOptions { Mode = Meziantou.Framework.Toml.Parsing.TomlParserMode.Tolerant },
            new TomlSerializerOptions { SourceName = "test.toml" });

        var events = new List<TomlParseEventKind>();
        Assert.DoesNotThrow(() =>
        {
            while (parser.MoveNext())
            {
                events.Add(parser.Current.Kind);
            }
        });

        Assert.True(parser.HasErrors);
        Assert.True(parser.Diagnostics.HasErrors);
        Assert.True(parser.Diagnostics.Count > 0);
        Assert.Contains("Missing value", parser.Diagnostics[0].Message);
        Assert.Equal(new[]
        {
            TomlParseEventKind.StartDocument,
            TomlParseEventKind.StartTable,
            TomlParseEventKind.PropertyName,
            TomlParseEventKind.EndTable,
            TomlParseEventKind.EndDocument,
        }, events);
    }

    [Fact]
    public void Deserialize_Object_ReadsNestedTablesAndArrays()
    {
        var result = TomlSerializer.Deserialize<object>(
            """
            title = "doc"
            values = [1, 2]

            [child]
            enabled = true
            """);

        Assert.IsType<TomlTable>(result);
        var root = (TomlTable)result!;
        Assert.Equal("doc", root["title"]);
        Assert.IsType<TomlArray>(root["values"]);
        Assert.IsType<TomlTable>(root["child"]);

        var child = (TomlTable)root["child"];
        Assert.Equal(true, child["enabled"]);
    }

    // BcWugYjVchJ and uAmGjGvd_lN have the same 64-bit FNV-1a hash
    [Theory]
    [InlineData("[BcWugYjVchJ]\nx = 1\n[uAmGjGvd_lN]\ny = 2\n")]
    [InlineData("BcWugYjVchJ.x = 1\nuAmGjGvd_lN.y = 2\n")]
    [InlineData("[BcWugYjVchJ.a]\nx = 1\n[uAmGjGvd_lN.a]\ny = 2\n")]
    [InlineData("[[BcWugYjVchJ]]\nx = 1\n[[uAmGjGvd_lN]]\ny = 2\n")]
    [InlineData("t = { BcWugYjVchJ.x = 1, uAmGjGvd_lN.y = 2 }\n")]
    public void Deserialize_KeysWithCollidingHashes_AreDistinct(string toml)
    {
        var root = TomlSerializer.Deserialize<TomlTable>(toml)!;
        if (root.TryGetValue("t", out var inline))
        {
            root = (TomlTable)inline;
        }

        Assert.HasCount(2, root);
        Assert.Equal(["x"], GetLeafKeys(root["BcWugYjVchJ"]));
        Assert.Equal(["y"], GetLeafKeys(root["uAmGjGvd_lN"]));

        static List<string> GetLeafKeys(object value)
        {
            var keys = new List<string>();
            switch (value)
            {
                case TomlTable table:
                    foreach (var item in table)
                    {
                        if (item.Value is TomlTable or TomlTableArray)
                        {
                            keys.AddRange(GetLeafKeys(item.Value));
                        }
                        else
                        {
                            keys.Add(item.Key);
                        }
                    }

                    break;

                case TomlTableArray array:
                    foreach (var item in array)
                    {
                        keys.AddRange(GetLeafKeys(item));
                    }

                    break;
            }

            return keys;
        }
    }

    [Theory]
    [InlineData("[a]\nx = 1\n[\"a\".b]\ny = 2\n")]
    [InlineData("[a]\nx = 1\n['a'.b]\ny = 2\n")]
    [InlineData("[a]\nx = 1\n[\"\\u0061\".b]\ny = 2\n")]
    [InlineData("\"\\u0061\".x = 1\na.b.y = 2\n")]
    public void Deserialize_QuotedKeysMatchBareKeys(string toml)
    {
        var root = TomlSerializer.Deserialize<TomlTable>(toml)!;

        Assert.Single(root);
        var a = (TomlTable)root["a"];
        Assert.Equal(1L, a["x"]);
        Assert.Equal(2L, ((TomlTable)a["b"])["y"]);
    }
}
