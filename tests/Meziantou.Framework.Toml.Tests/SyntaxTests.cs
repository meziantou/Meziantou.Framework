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
}
