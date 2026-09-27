using System;
using System.Collections.Generic;
using System.Linq;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Tests;

public class SyntaxTests
{
    [Theory]
    [InlineData("note\nadmin = true")]
    [InlineData("note\r")]
    [InlineData("\u007F")]
    public void Comment_WithControlCharacter_Throws(string comment)
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Comment(comment));
    }

    [Fact]
    public void Comment_WithUnpairedSurrogate_Throws()
    {
        // Built here: InlineData goes through UTF-8, which replaces lone surrogates
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Comment("a" + '\uD800'));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Comment('\uDC00' + "a"));
        Assert.Equal("# \uD83D\uDE00", SyntaxFactory.Comment("\uD83D\uDE00").Text);
    }

    [Fact]
    public void ParseStrict_TextReader()
    {
        using (var reader = new System.IO.StringReader("a = 1\n"))
        {
            Assert.False(SyntaxParser.ParseStrict(reader).HasErrors);
        }

        using (var reader = new System.IO.StringReader("a = \n"))
        {
            Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict(reader, TomlSerializerOptions.Default, "config.toml"));
        }
    }

    [Fact]
    public void Comment_WithTab_IsAccepted()
    {
        Assert.Equal("# a\tb", SyntaxFactory.Comment("a\tb").Text);
    }

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
    public void UnexpectedToken_InNestedContainers_IsReportedOnceWithABoundedText()
    {
        var token = new string('x', 100_000);
        var toml = "a = " + string.Concat(Enumerable.Repeat("{a=", 40)) + " 1 " + token + "\n";

        var doc = SyntaxParser.Parse(toml);

        Assert.True(doc.HasErrors);
        Assert.HasCountLessThan(5, doc.Diagnostics);
        Assert.True(doc.Diagnostics.Sum(diagnostic => diagnostic.Message.Length) < 2_000, doc.Diagnostics.ToString());
    }

    [Fact]
    public void TomlException_MessageLength_IsBounded()
    {
        var diagnostics = new DiagnosticsBag();
        for (var i = 0; i < 50; i++)
        {
            diagnostics.Error(new SourceSpan("", new TextPosition(0, 0, 0), new TextPosition(0, 0, 0)), new string('m', 10_000));
        }

        var exception = new TomlException(diagnostics);

        Assert.HasCountLessThan(150_000, exception.Message);
        Assert.EndsWith("more diagnostics.", exception.Message.TrimEnd());
    }

    [Fact]
    public void TomlException_SingleLongDiagnostic_IsTruncated()
    {
        var diagnostics = new DiagnosticsBag();
        diagnostics.Error(new SourceSpan("", new TextPosition(0, 0, 0), new TextPosition(0, 0, 0)), new string('m', 1_000_000));

        var exception = new TomlException(diagnostics);

        Assert.HasCountLessThan(100_100, exception.Message);
    }

    [Theory]
    [InlineData("a = 0", "1")]
    [InlineData("a = 1979-05-27T07:32:00", "1")]
    [InlineData("a = 1e", "9")]
    [InlineData("a = 1 \"", "x")]
    public void LongTokens_AreTruncatedInMessages(string prefix, string repeated)
    {
        var toml = prefix + string.Concat(Enumerable.Repeat(repeated, 1_000_000)) + "\n";

        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var parseStrict = Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict(toml));

        Assert.HasCountLessThan(1_000, deserialize.Message);
        Assert.HasCountLessThan(1_000, parseStrict.Message);
    }

    [Fact]
    public void LongInputValues_AreTruncatedInSerializerMessages()
    {
        var toml = "value = '" + new string('x', 1_000_000) + "'\n";

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Dictionary<string, DayOfWeek>>(toml));

        Assert.HasCountLessThan(1_000, exception.Message);
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
    [InlineData("x = 1\na = [\n")]
    [InlineData("x = 1\na = { b = \n")]
    [InlineData("x = 1\n[a\n")]
    public void Parse_ErrorsAfterTheEndOfFile_AreReportedAtTheEndOfFile(string toml)
    {
        var doc = SyntaxParser.Parse(toml);

        Assert.NotEmpty(doc.Diagnostics);
        Assert.All(doc.Diagnostics, diagnostic => Assert.NotEqual(0, diagnostic.Span.Start.Line));
        Assert.Equal(toml, doc.ToString());
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

    [Theory]
    [InlineData("a = \"\\")]
    [InlineData("a = \"\"\"x\\")]
    public void Parse_EscapeAtEndOfFile_DoesNotReportAControlCharacter(string toml)
    {
        var doc = SyntaxParser.Parse(toml);

        Assert.Contains(doc.Diagnostics, diagnostic => diagnostic.Message.Contains("end of file in an escape sequence", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Diagnostics, diagnostic => diagnostic.Message.Contains("control character", StringComparison.Ordinal));
        Assert.Equal(toml, doc.ToString());
    }
}
