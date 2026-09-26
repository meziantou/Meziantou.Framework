using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Tests;

public class SyntaxTests
{
    [Fact]
    public void TestDocument()
    {
        var table = new TableSyntax("test")
        {
            Items =
            {
                {"a", 1},
                {"b", true},
                {"c", "Check"},
                {"d", "ToEscape\nWithAnotherChar\t"},
                {"e", 12.5},
                {"f", new int[] {1, 2, 3, 4}},
                {"g", new string[] {"0", "1", "2"}},
                {"key with space", 2}
            }
        };

        var doc = new DocumentSyntax()
        {
            Tables =
            {
                table
            }
        };

        table.AddLeadingComment("This is a comment");
        table.AddLeadingTriviaNewLine();

        var firstElement = table.Items.GetChild(0)!;
        firstElement.AddTrailingComment("This is an item comment");

        var secondElement = table.Items.GetChild(2)!;
        secondElement.AddLeadingTriviaNewLine();
        secondElement.AddLeadingComment("This is a comment in a middle of a table");
        secondElement.AddLeadingTriviaNewLine();
        secondElement.AddLeadingTriviaNewLine();

        var docStr = doc.ToString();

        var expected = @"# This is a comment
[test]
a = 1 # This is an item comment
b = true

# This is a comment in a middle of a table

c = ""Check""
d = ""ToEscape\nWithAnotherChar\t""
e = 12.5
f = [1, 2, 3, 4]
g = [""0"", ""1"", ""2""]
""key with space"" = 2
";

        AssertHelper.AreEqualNormalizeNewLine(expected, docStr);

        // Reparse the result and compare it again
        var newDoc = SyntaxParser.Parse(docStr);
        AssertHelper.AreEqualNormalizeNewLine(expected, newDoc.ToString());
    }

    [Fact]
    public void Sample()
    {
        var input = @"[mytable]
key = 15
val = true
";

        // Gets a syntax tree of the TOML text
        var doc = SyntaxParser.Parse(input); // returns a DocumentSyntax
        // Check for parsing errors with doc.HasErrors and doc.Diagnostics
        // doc.HasErrors => throws an exception

        // Prints the exact representation of the input
        var docStr = doc.ToString();
        TestContext.Current.TestOutputHelper?.WriteLine(docStr);

        // Gets a runtime representation of the syntax tree
        var table = TomlSerializer.Deserialize<TomlTable>(input);
        Assert.NotNull(table);
        var nonNullTable = table!;
        var key = (long)((TomlTable)nonNullTable["mytable"]!)["key"]!;
        var value = (bool)((TomlTable)nonNullTable["mytable"]!)["val"]!;
        TestContext.Current.TestOutputHelper?.WriteLine($"key = {key}, val = {value}");
    }

    [Fact]
    public void Parse_LexerWithoutDecodedScalars_DecodesStrings()
    {
        var toml = "\"a\" = \"x\\ty\"\n'b' = '''c'''\n\"c\" = \"\"\"d\\u0041\"\"\"\n";

        var doc = SyntaxParser.Parse(TomlLexer.Create(toml));

        Assert.False(doc.HasErrors, message: string.Join(Environment.NewLine, doc.Diagnostics));
        var values = doc.KeyValues.Select(kv => (((StringValueSyntax)kv.Key!.Key!).Value, ((StringValueSyntax)kv.Value!).Value)).ToArray();
        Assert.Equal([("a", "x\ty"), ("b", "c"), ("c", "dA")], values);
        Assert.Equal(toml, doc.ToString());
    }

    [Fact]
    public void KeySyntax_DotKeys_AreCreatedOnlyForDottedKeys()
    {
        var toml = "a = 1\nb.c = 2\n";

        var doc = SyntaxParser.Parse(toml);

        var simple = doc.KeyValues.GetChild(0)!.Key!;
        var dotted = doc.KeyValues.GetChild(1)!.Key!;
        Assert.Null(simple.GetChild(1));
        Assert.Equal(1, dotted.DotKeys.ChildrenCount);
        Assert.Equal(0, simple.DotKeys.ChildrenCount);
        Assert.Equal(toml, doc.ToString());
    }

    [Theory]
    [InlineData("a = \"x#y\"\n", 0x01, "\\u0001")]
    [InlineData("a = 'x#y'\n", 0x7F, "\\u007F")]
    [InlineData("a = \"\"\"\nline\nx#y\"\"\"\n", 0x01, "\\u0001")]
    [InlineData("a = '''\nline\nx#y'''\n", 0x7F, "\\u007F")]
    public void Parse_ControlCharacterInString_IsReportedAtTheCharacter(string template, int controlCharacter, string printable)
    {
        var toml = template.Replace('#', (char)controlCharacter);
        var index = toml.IndexOf((char)controlCharacter, StringComparison.Ordinal);

        var doc = SyntaxParser.Parse(toml);

        var diagnostic = Assert.Single(doc.Diagnostics);
        Assert.Equal($"Invalid control character found {printable}", diagnostic.Message);
        Assert.Equal(toml.AsSpan(0, index).Count('\n'), diagnostic.Span.Start.Line);
        Assert.Equal(index - toml.AsSpan(0, index).LastIndexOf('\n') - 1, diagnostic.Span.Start.Column);
    }

    [Fact]
    public void InlineTableSyntax_FromKeyValues_IsWrittenOnOneLine()
    {
        var inlineTable = new InlineTableSyntax(new KeyValueSyntax("a", new IntegerValueSyntax(1)), new KeyValueSyntax("b", new IntegerValueSyntax(2)));
        var doc = new DocumentSyntax();
        doc.KeyValues.Add(new KeyValueSyntax("t", inlineTable));

        Assert.Equal("{ a = 1, b = 2 }", inlineTable.ToString());
        Assert.Equal("t = { a = 1, b = 2 }\n", doc.ToString().ReplaceLineEndings("\n"));
        Assert.False(SyntaxParser.Parse(doc.ToString()).HasErrors);
    }

    [Fact]
    public void RunOfInvalidCharacters_IsReportedOnce()
    {
        var comment = "# " + new string('\0', 100_000) + "\na = 1\n";
        var inString = "a = \"" + new string('\u0001', 1000) + "\"\n";

        var strict = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(comment));
        var commentDoc = SyntaxParser.Parse(comment);
        var stringDoc = SyntaxParser.Parse(inString);

        Assert.Single(strict.Diagnostics);
        Assert.HasCountLessThan(200, strict.Message);
        var commentDiagnostic = Assert.Single(commentDoc.Diagnostics);
        Assert.Equal(2, commentDiagnostic.Span.Start.Column);
        Assert.Equal(100_001, commentDiagnostic.Span.End.Column);
        Assert.Single(stringDoc.Diagnostics);
    }

    [Fact]
    public void TomlException_MessageListsTheFirstDiagnosticsOnly()
    {
        var comment = "# " + string.Concat(Enumerable.Repeat("\0a", 1000)) + "\n";

        var doc = SyntaxParser.Parse(comment);
        var exception = new TomlException(doc.Diagnostics);

        Assert.HasCount(1000, doc.Diagnostics);
        Assert.HasCount(1000, exception.Diagnostics);
        Assert.HasCount(101, exception.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.EndsWith("... and 900 more diagnostics.", exception.Message.TrimEnd());
    }

    [Theory]
    [InlineData("\\u00", "Invalid escape `\\u`. Expected 4 hexadecimal digits.")]
    [InlineData("\\U0001F6", "Invalid escape `\\U`. Expected 8 hexadecimal digits.")]
    [InlineData("\\x4", "Invalid escape `\\x`. Expected 2 hexadecimal digits.")]
    public void Parse_ShortEscape_DoesNotSwallowTheRestOfTheDocument(string escape, string message)
    {
        var toml = "a = \"" + escape + "\"\nb = 1\nc = 2\n";

        var doc = SyntaxParser.Parse(toml);

        var diagnostic = Assert.Single(doc.Diagnostics);
        Assert.Equal(message, diagnostic.Message);
        Assert.Equal(3, doc.KeyValues.ChildrenCount);
        Assert.Equal(toml, doc.ToString());
    }
}
