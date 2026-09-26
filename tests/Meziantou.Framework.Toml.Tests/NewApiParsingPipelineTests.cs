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

    [Theory]
    [InlineData(TomlLexerMode.Key)]
    [InlineData(TomlLexerMode.Value)]
    public void TomlLexer_InvalidNonBmpCharacter_CoversTheSurrogatePair(TomlLexerMode mode)
    {
        var character = char.ConvertFromUtf32(0x1F600);
        var lexer = TomlLexer.Create(character + " ");
        lexer.Mode = mode;

        Assert.True(lexer.MoveNext());
        Assert.Equal(TokenKind.Invalid, lexer.Current.Kind);
        Assert.Equal(character, lexer.GetText(lexer.Current));
        Assert.True(lexer.MoveNext());
        Assert.Equal(2, lexer.Current.Start.Offset);
    }

    [Fact]
    public void SyntaxParser_InvalidNonBmpCharacter_IsReportedWhole()
    {
        var character = char.ConvertFromUtf32(0x1F600);

        var doc = SyntaxParser.Parse($"a = {character}\n");

        Assert.Contains(doc.Diagnostics, diagnostic => diagnostic.Message.Contains(character, StringComparison.Ordinal));
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

    [Theory]
    [InlineData("a = 1\na = 2\n")]
    [InlineData("a = 1\n'a' = 2\n")]
    [InlineData("[a]\nx = 1\n[a]\ny = 2\n")]
    [InlineData("t = {a = 1, a = 2}\n")]
    [InlineData("t = {a.b = 1, a = 2}\n")]
    [InlineData("a = 1\n[a.b.c]\n")]
    [InlineData("a = [1]\n[[a]]\n")]
    [InlineData("a = [{b = 1}]\n[a.c]\n")]
    [InlineData("a.b = 1\n[a]\n")]
    [InlineData("[a]\nb.c = 1\n[a.b]\n")]
    [InlineData("sub = {x = 1}\n[sub]\nw = 2\n")]
    [InlineData("sub = {x = 1}\nsub.w = 2\n")]
    [InlineData("[[a]]\n[a]\n")]
    [InlineData("[a]\n[[a]]\n")]
    [InlineData("[a.b.c]\nz = 9\n[a]\nb.c.t = 1\n")]
    [InlineData("[a.b]\n[a]\nb.c = 1\n[a.b]\n")]
    [InlineData("[[a]]\nb = {}\n[[c]]\n[a.b.d]\n")]
    public void Deserialize_RedefinedKey_Throws(string toml)
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Dictionary<string, object>>(toml));
    }

    [Theory]
    [InlineData("[a.b]\n[a]\n")]
    [InlineData("[a]\nb.c = 1\nb.d = 2\n[a.b.e]\n")]
    [InlineData("[a.b.c]\n[a]\nb.d = 1\n")]
    [InlineData("[[a]]\nb = 1\n[[a]]\nb = 2\n[a.c]\n")]
    [InlineData("a = [{b = 1, c.d = 1}, {b = 1, c.d = 1}]\n")]
    [InlineData("t = {a.b = 1, a.c = 2}\n")]
    public void Deserialize_ExtendedTable_IsValid(string toml)
    {
        Assert.NotNull(TomlSerializer.Deserialize<TomlTable>(toml));
    }

    [Fact]
    public void Deserialize_RedefinedKey_ReportsBothLocations()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>("a = 1\nb = 2\na = 3\n"));

        Assert.Equal(3, ex.Line);
        Assert.Contains("The key `a` is already defined at (1,1)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_LastWins_AcceptsRedefinedValues()
    {
        var options = new TomlSerializerOptions { DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins };

        var root = TomlSerializer.Deserialize<TomlTable>("a = 1\na = 2\nt = {x = 1}\nt = {x = 2, y = 3}\n", options)!;

        Assert.Equal(2L, root["a"]);
        var t = (TomlTable)root["t"];
        Assert.Equal(2L, t["x"]);
        Assert.Equal(3L, t["y"]);
    }

    [Theory]
    [InlineData("a = 1\n[a]\n")]
    [InlineData("a = 1\na.b = 2\n")]
    [InlineData("[a]\nb.c = 1\n[a.b]\n")]
    [InlineData("[[a]]\n[a]\n")]
    [InlineData("[a]\n[a]\n")]
    public void Deserialize_LastWins_RejectsConflictingDefinitions(string toml)
    {
        var options = new TomlSerializerOptions { DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins };

        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml, options));
    }

    [Fact]
    public void TomlParser_Tolerant_ReportsRedefinedKey()
    {
        var parser = TomlParser.Create("a = 1\na = 2\n", new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        while (parser.MoveNext())
        {
        }

        Assert.True(parser.HasErrors);
    }

    [Fact]
    public void TomlReader_IsInlineContainer_DistinguishesInlineContainersFromHeaders()
    {
        var reader = TomlReader.Create("a = {x = [1]}\nb.c = 1\n[d]\n[[e]]\n");
        var containers = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
            {
                containers.Add($"{reader.TokenType}:{reader.IsInlineContainer}:{reader.CurrentSpan?.Start.Line}");
            }
            else
            {
                Assert.False(reader.IsInlineContainer);
            }
        }

        Assert.Equal(
            ["StartTable:False:", "StartTable:True:0", "StartArray:True:0", "StartTable:False:1", "StartTable:False:2", "StartArray:False:3", "StartTable:False:3"],
            containers);
    }
}
