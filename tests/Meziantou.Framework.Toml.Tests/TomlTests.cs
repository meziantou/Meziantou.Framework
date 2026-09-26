using System;
using System.Linq;
using System.Text;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Tests;

/// <summary>
/// Tests for the syntax parsing frontend.
/// </summary>
public class TomlTests
{
    [Fact]
    public void TestDescendants()
    {
        var input = @"# This is a comment
[table]
key = 1 # This is another comment
test.sub.key = ""yes""
[[array]]
hello = true
";
        var expected = """
                       trivia: (1,1)-(1,19)  Comment "# This is a comment"
                       trivia: (1,20)-(1,21)  NewLine "\r\n"
                       token: (2,1)-(2,1) "["
                       token: (2,2)-(2,6) "table"
                       token: (2,7)-(2,7) "]"
                       token: (2,8)-(2,9) "\r\n"
                       token: (3,1)-(3,3) "key"
                       trivia: (3,4)-(3,4)  Whitespaces " "
                       token: (3,5)-(3,5) "="
                       trivia: (3,6)-(3,6)  Whitespaces " "
                       token: (3,7)-(3,7) "1"
                       trivia: (3,8)-(3,8)  Whitespaces " "
                       trivia: (3,9)-(3,33)  Comment "# This is another comment"
                       token: (3,34)-(3,35) "\r\n"
                       token: (4,1)-(4,4) "test"
                       token: (4,5)-(4,5) "."
                       token: (4,6)-(4,8) "sub"
                       token: (4,9)-(4,9) "."
                       token: (4,10)-(4,12) "key"
                       trivia: (4,13)-(4,13)  Whitespaces " "
                       token: (4,14)-(4,14) "="
                       trivia: (4,15)-(4,15)  Whitespaces " "
                       token: (4,16)-(4,20) "\"yes\""
                       token: (4,21)-(4,22) "\r\n"
                       token: (5,1)-(5,2) "[["
                       token: (5,3)-(5,7) "array"
                       token: (5,8)-(5,9) "]]"
                       token: (5,10)-(5,11) "\r\n"
                       token: (6,1)-(6,5) "hello"
                       trivia: (6,6)-(6,6)  Whitespaces " "
                       token: (6,7)-(6,7) "="
                       trivia: (6,8)-(6,8)  Whitespaces " "
                       token: (6,9)-(6,12) "true"
                       token: (6,13)-(6,14) "\r\n"

                       """;
        AssertDocumentSyntax(expected, input);
    }

    [Fact]
    public void TestInlineArray()
    {
        var input = @"x = [1,
2,
3
]
";
        var expected = """
                       token: (1,1)-(1,1) "x"
                       trivia: (1,2)-(1,2)  Whitespaces " "
                       token: (1,3)-(1,3) "="
                       trivia: (1,4)-(1,4)  Whitespaces " "
                       token: (1,5)-(1,5) "["
                       token: (1,6)-(1,6) "1"
                       token: (1,7)-(1,7) ","
                       trivia: (1,8)-(1,9)  NewLine "\r\n"
                       token: (2,1)-(2,1) "2"
                       token: (2,2)-(2,2) ","
                       trivia: (2,3)-(2,4)  NewLine "\r\n"
                       token: (3,1)-(3,1) "3"
                       trivia: (3,2)-(3,3)  NewLine "\r\n"
                       token: (4,1)-(4,1) "]"
                       token: (4,2)-(4,3) "\r\n"

                       """;
        AssertDocumentSyntax(expected, input);
    }

    private static void AssertDocumentSyntax(string expected, string input)
    {
        input = input.ReplaceLineEndings("\r\n");
        var doc = SyntaxParser.Parse(input);
        var tokens = doc.Tokens().ToList();
        var builder = new StringBuilder();
        foreach (var node in tokens)
        {
            if (node is SyntaxTrivia trivia)
            {
                builder.AppendLine($"trivia: {trivia.Span}  {trivia.Kind} {(trivia.Text is not null ? TomlFormatHelper.ToString(trivia.Text, TomlPropertyDisplayKind.Default) : string.Empty)}");
            }
            else if (node is SyntaxToken token)
            {
                builder.AppendLine($"token: {token.Span} {(token.Text is not null ? TomlFormatHelper.ToString(token.Text, TomlPropertyDisplayKind.Default) : string.Empty)}");
            }
        }

        var docStr = builder.ToString();
        AssertHelper.AreEqualNormalizeNewLine(expected, docStr, true);
    }

    [Theory]
    [InlineData("a\r\nb", TomlPropertyDisplayKind.StringMulti, "\"\"\"\na\r\nb\"\"\"")]
    [InlineData("\r\n\u0001", TomlPropertyDisplayKind.StringMulti, "\"\"\"\n\r\n\\u0001\"\"\"")]
    [InlineData("a\r\nb", TomlPropertyDisplayKind.Default, "\"a\\r\\nb\"")]
    public void TomlFormatHelper_String_WritesEachCharacterOnce(string value, TomlPropertyDisplayKind displayKind, string expected)
    {
        Assert.Equal(expected, TomlFormatHelper.ToString(value, displayKind));
    }

    [Fact]
    public void TomlFormatHelper_ValuesTomlCannotRepresent_Throw()
    {
        Assert.Throws<TomlException>(() => TomlFormatHelper.ToString("a\uD800b", TomlPropertyDisplayKind.Default));
        Assert.Throws<TomlException>(() => TomlFormatHelper.ToString(ulong.MaxValue, TomlPropertyDisplayKind.Default));
        Assert.Equal("9223372036854775807", TomlFormatHelper.ToString((ulong)long.MaxValue, TomlPropertyDisplayKind.Default));
    }
}
