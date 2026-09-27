using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public void Token_WithoutAPredefinedText_ThrowsForTheKindParameter()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => SyntaxFactory.Token(TokenKind.NewLine));

        Assert.Equal("kind", exception.ParamName);
        Assert.Equal(TokenKind.NewLine, exception.ActualValue);
    }

    [Fact]
    public void LeadingComment_DoesNotCommentOutTheNode()
    {
        var doc = new DocumentSyntax();
        doc.KeyValues.Add(new KeyValueSyntax("a", new IntegerValueSyntax(1)).AddLeadingComment("hello"));
        doc.KeyValues.Add(new KeyValueSyntax("b", new IntegerValueSyntax(2)));
        var table = new TableSyntax("t") { Items = { { "x", 1 } } }.AddLeadingComment("header");
        doc.Tables.Add(table);

        var toml = doc.ToString();
        var parsed = TomlSerializer.Deserialize<TomlTable>(toml)!;

        Assert.Equal("# hello\na = 1\nb = 2\n# header\n[t]\nx = 1\n", toml.ReplaceLineEndings("\n"));
        Assert.Equal(1L, parsed["a"]);
        Assert.Equal(1L, ((TomlTable)parsed["t"]!)["x"]);
    }

    [Fact]
    public void TrailingComment_InAnInlineTable_DoesNotCommentOutTheNextItems()
    {
        var inlineTable = new InlineTableSyntax(new KeyValueSyntax("x", new IntegerValueSyntax(1)), new KeyValueSyntax("y", new IntegerValueSyntax(2)));
        inlineTable.Items.GetChild(0)!.KeyValue!.Value!.AddTrailingComment("c");
        var doc = new DocumentSyntax();
        doc.KeyValues.Add(new KeyValueSyntax("a", inlineTable));

        var parsed = TomlSerializer.Deserialize<TomlTable>(doc.ToString())!;

        Assert.Equal(2L, ((TomlTable)parsed["a"]!)["y"]);
    }

    [Fact]
    public void BareKey_WithInvalidCharacters_ThrowsForTheNameParameter()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BareKeySyntax("a b\0" + new string('x', 1_000_000)));

        Assert.Equal("name", exception.ParamName);
        Assert.DoesNotContain('\0', exception.Message);
        Assert.HasCountLessThan(1_000, exception.Message);
    }

    [Fact]
    public void GetChild_PastTheLastChild_Throws()
    {
        var doc = SyntaxParser.Parse("a = 1\n");
        var keyValue = doc.KeyValues.GetChild(0)!;

        Assert.Throws<ArgumentOutOfRangeException>(() => keyValue.GetChild(keyValue.ChildrenCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetChild(doc.ChildrenCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.KeyValues.GetChild(doc.KeyValues.ChildrenCount));
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

    // The first error of SyntaxParser is the first one of the document, as for TomlParser
    [Theory]
    [InlineData("a = 0123\nb = [1 2]\n")]
    [InlineData("x = 1\na = 1__0\n[b\n")]
    public void ParseStrict_ReportsTheFirstErrorOfTheDocument(string toml)
    {
        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var parseStrict = Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict(toml));
        var doc = SyntaxParser.Parse(toml);

        Assert.Equal(deserialize.Line, parseStrict.Line);
        Assert.Equal(doc.Diagnostics.Select(diagnostic => diagnostic.Span.Start.Offset).Order().ToArray(), doc.Diagnostics.Select(diagnostic => diagnostic.Span.Start.Offset).ToArray());
    }

    // A missing comma is reported once, whatever the kind of the next value
    [Theory]
    [InlineData("1")]
    [InlineData("inf")]
    [InlineData("nan")]
    [InlineData("+nan")]
    [InlineData("-nan")]
    [InlineData("'a'")]
    public void MissingCommaBeforeAValue_IsReportedOnce(string value)
    {
        var doc = SyntaxParser.Parse($"a = [1 {value}, 2]\n");

        Assert.Single(doc.Diagnostics);
    }

    [Theory]
    [InlineData("a = \"\\😀\"\n", 6, 7)]
    [InlineData("a = \"\\q\"\n", 6, 6)]
    public void InvalidEscapeCharacter_IsCoveredWhole(string toml, int startOffset, int endOffset)
    {
        var diagnostic = Assert.Single(SyntaxParser.Parse(toml).Diagnostics, diagnostic => diagnostic.Message.StartsWith("Unexpected escape character", StringComparison.Ordinal));
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));

        Assert.Equal((startOffset, endOffset), (diagnostic.Span.Start.Offset, diagnostic.Span.End.Offset));
        Assert.Equal((startOffset, endOffset), (exception.Diagnostics![0].Span.Start.Offset, exception.Diagnostics[0].Span.End.Offset));
    }

    [Theory]
    [InlineData('\0', "\\u0000")]
    [InlineData('\u001B', "\\u001B")]
    [InlineData('\u007F', "\\u007F")]
    public void InvalidEscapeOfAControlCharacter_IsPrintableInMessages(char character, string expected)
    {
        var toml = $"a = \"\\{character}\"\n";

        var diagnostic = SyntaxParser.Parse(toml).Diagnostics.First(diagnostic => diagnostic.Message.StartsWith("Unexpected escape character", StringComparison.Ordinal));

        Assert.DoesNotContain(character, diagnostic.Message);
        Assert.Contains($"[{expected}]", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RedefinedLongKeys_AreTruncatedInMessages()
    {
        var key = new string('k', 1_000_000);
        var toml = $"{key} = 1\n{key} = 2\n";

        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var doc = SyntaxParser.Parse(toml);

        Assert.HasCountLessThan(1_000, deserialize.Message);
        Assert.HasCountLessThan(1_000, Assert.Single(doc.Diagnostics).Message);
        Assert.HasCountLessThan(100_100, new TomlException(new TomlSourceSpan(), new string('m', 1_000_000)).Message);
    }

    // Like TomlParser, SyntaxParser reports a redefined key once, on its first redefined segment
    [Fact]
    public void DottedKeyThroughAnInlineTable_IsReportedOnce()
    {
        var toml = "a = { b = { c = { d = {} } } }\na.b.c.d.e = 1\n";

        var doc = SyntaxParser.Parse(toml);
        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));

        var diagnostic = Assert.Single(doc.Diagnostics);
        Assert.Equal((deserialize.Line, deserialize.Column), (diagnostic.Span.Start.Line + 1, diagnostic.Span.Start.Column + 1));
        Assert.Contains("The key `a` is already defined", diagnostic.Message, StringComparison.Ordinal);
    }

    // A strict parser keeps one error, however many errors a number has
    [Fact]
    public void NumberWithManyErrors_DoesNotHoldEveryError()
    {
        var toml = "a = 1" + string.Concat(Enumerable.Repeat("__1", 300_000)) + "\n";
        // Without errors, the number only overflows
        var valid = "a = 1" + string.Concat(Enumerable.Repeat("_11", 300_000)) + "\n";
        _ = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>("a = 1__1\n"));
        _ = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(valid));

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(valid));
        var validAllocations = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var allocations = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Single(exception.Diagnostics!);
        Assert.True(allocations < 2 * validAllocations, $"The errors allocated {allocations} bytes, and a number without errors {validAllocations} bytes");
    }

    [Theory]
    [InlineData("a = 0b___1\n", 6, 8)]
    [InlineData("a = 0x_1\n", 6, 6)]
    [InlineData("a = 0o7__\n", 8, 8)]
    public void RunOfMisplacedUnderscoresInARadixNumber_IsReportedOnce(string toml, int startOffset, int endOffset)
    {
        var diagnostic = Assert.Single(SyntaxParser.Parse(toml).Diagnostics, diagnostic => diagnostic.Message.StartsWith("An underscore", StringComparison.Ordinal));

        Assert.Equal((startOffset, endOffset), (diagnostic.Span.Start.Offset, diagnostic.Span.End.Offset));
    }

    [Fact]
    public void RadixNumberWithManyMisplacedUnderscores_DoesNotAllocatePerUnderscore()
    {
        var toml = "a = 0x" + new string('_', 1_000_000) + "1\n";
        var valid = "a = 0x" + new string('1', 1_000_001) + "\n";
        _ = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>("a = 0x__1\n"));

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(valid));
        var validAllocations = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        _ = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var allocations = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocations < 2 * validAllocations + 100_000, $"The errors allocated {allocations} bytes, and a number without errors {validAllocations} bytes");
    }

    // Reading a number is linear in its length, whatever its first digit: the time is compared with a number of the same
    // length that starts with 1
    [Theory]
    [InlineData("a = 0", '1', "")]
    [InlineData("a = -0", '0', "")]
    [InlineData("a = 0", '1', ".5")]
    public void LongNumberWithALeadingZero_IsReadInLinearTime(string prefix, char digit, string suffix)
    {
        const int Length = 4_000_000;
        var toml = prefix + new string(digit, Length) + suffix + "\n";
        var control = "a = 1" + new string(digit, Length) + suffix + "\n";

        var time = Measure(toml);
        var controlTime = Measure(control);

        Assert.True(time < (5 * controlTime) + TimeSpan.FromMilliseconds(200), $"{time.TotalMilliseconds} ms, and {controlTime.TotalMilliseconds} ms for a number that starts with 1");

        static TimeSpan Measure(string toml)
        {
            var min = TimeSpan.MaxValue;
            for (var i = 0; i < 3; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    _ = TomlSerializer.Deserialize<TomlTable>(toml);
                }
                catch (TomlException)
                {
                }

                min = TimeSpan.FromTicks(Math.Min(min.Ticks, stopwatch.Elapsed.Ticks));
            }

            return min;
        }
    }

    // Like the errors of a number, the errors of any token are listed in the order of the document. {S} stands for a lone
    // surrogate, which InlineData cannot hold.
    [Theory]
    [InlineData("a = \"\\")]
    [InlineData("a = \"\\u12")]
    [InlineData("a = \"\"\"\\")]
    [InlineData("a = \"\\x4{S}\"\n")]
    [InlineData("a = \"\\uD800{S}\"\n")]
    [InlineData("a = 1\r{S}\n")]
    public void ErrorsOfAToken_AreInDocumentOrder(string template)
    {
        var toml = template.Replace("{S}", "\uD800", StringComparison.Ordinal);

        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var parseStrict = Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict(toml));
        var parser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        while (parser.MoveNext())
        {
        }

        Assert.Equal((parseStrict.Line, parseStrict.Column, parseStrict.Diagnostics![0].Message), (deserialize.Line, deserialize.Column, deserialize.Diagnostics![0].Message));
        var offsets = parser.Diagnostics.Select(diagnostic => diagnostic.Span.Start.Offset).ToArray();
        Assert.Equal(offsets.Order().ToArray(), offsets);
    }

    // {S} and {L} stand for lone surrogates, which InlineData cannot hold
    [Theory]
    [InlineData("{S}")]
    [InlineData("a{S} = 1")]
    [InlineData("a = 1 {L}\n")]
    [InlineData("{S} = 1")]
    [InlineData("a = 😀{S}")]
    public void LoneSurrogates_AreEscapedInMessages(string template)
    {
        var toml = template.Replace("{S}", "\uD800", StringComparison.Ordinal).Replace("{L}", "\uDC00", StringComparison.Ordinal);
        var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var parseStrict = Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict(toml));

        foreach (var diagnostic in SyntaxParser.Parse(toml).Diagnostics)
        {
            _ = encoding.GetBytes(diagnostic.Message);
        }

        _ = encoding.GetBytes(deserialize.Message);
        _ = encoding.GetBytes(parseStrict.Message);
    }

    [Fact]
    public void SurrogatePairs_AreKeptInMessages()
    {
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>("a = 😀"));

        Assert.Contains("😀", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{S}")]
    [InlineData("{S} = 1")]
    [InlineData("\uFEFF{S}")]
    [InlineData("{L}# c")]
    public void LoneSurrogateAsTheFirstCharacter_IsReported(string template)
    {
        var toml = template.Replace("{S}", "\uD800", StringComparison.Ordinal).Replace("{L}", "\uDC00", StringComparison.Ordinal);

        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var doc = SyntaxParser.Parse(toml);

        Assert.Equal("Invalid UTF-16 surrogate sequence in TOML input.", deserialize.Diagnostics![0].Message);
        Assert.Equal("Invalid UTF-16 surrogate sequence in TOML input.", doc.Diagnostics[0].Message);
    }

    // A run of the same error is merged the same way in every path, whatever errors the path drops
    [Theory]
    [InlineData("a = \"x\r\r\"\n")]
    [InlineData("\r\r")]
    [InlineData("# \0\0\0\n")]
    public void FirstErrorOfARun_HasTheSameSpanInEveryPath(string toml)
    {
        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var parseStrict = Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict(toml));
        var parser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        while (parser.MoveNext())
        {
        }

        var expected = deserialize.Diagnostics![0].Span;
        Assert.Equal((expected.Start.Offset, expected.End.Offset), (parseStrict.Diagnostics![0].Span.Start.Offset, parseStrict.Diagnostics[0].Span.End.Offset));
        Assert.Equal((expected.Start.Offset, expected.End.Offset), (parser.Diagnostics[0].Span.Start.Offset, parser.Diagnostics[0].Span.End.Offset));
    }

    // The errors of a number are listed in the order of the document, and a leading zero is reported once
    [Theory]
    [InlineData("a = 0001\n")]
    [InlineData("a = 000\n")]
    [InlineData("a = 01__2\n")]
    [InlineData("a = 00.5\n")]
    [InlineData("a = 00.\n")]
    [InlineData("a = 00e\n")]
    [InlineData("a = 0_0_1\n")]
    public void ErrorsOfANumber_AreInOrderAndReportALeadingZeroOnce(string toml)
    {
        var deserialize = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
        var parseStrict = Assert.Throws<TomlException>(() => SyntaxParser.ParseStrict(toml));
        var doc = SyntaxParser.Parse(toml);
        var parser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        while (parser.MoveNext())
        {
        }

        Assert.Equal((deserialize.Line, deserialize.Column), (parseStrict.Line, parseStrict.Column));
        Assert.Equal((1, toml.IndexOf('0', StringComparison.Ordinal) + 1), (deserialize.Line, deserialize.Column));
        Assert.Single(doc.Diagnostics, diagnostic => diagnostic.Message.Contains("leading zero", StringComparison.Ordinal));
        AssertInOrder(doc.Diagnostics);
        AssertInOrder(parser.Diagnostics);
        AssertInOrder(deserialize.Diagnostics!);

        static void AssertInOrder(IEnumerable<DiagnosticMessage> diagnostics)
        {
            var offsets = diagnostics.Select(diagnostic => diagnostic.Span.Start.Offset).ToArray();
            Assert.Equal(offsets.Order().ToArray(), offsets);
        }
    }

    // The offset, the line and the column of a position designate the same character
    [Theory]
    [InlineData("a = '''abc\n\n")]
    [InlineData("a = \"abc")]
    [InlineData("a = \"abc\n")]
    [InlineData("a = \"\"\"abc\n")]
    [InlineData("a = '''x\r\n")]
    public void EndOfFileErrors_HaveConsistentCoordinates(string toml)
    {
        var doc = SyntaxParser.Parse(toml);
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));

        foreach (var diagnostic in doc.Diagnostics)
        {
            AssertConsistent(diagnostic.Span.Start.Offset, diagnostic.Span.Start.Line, diagnostic.Span.Start.Column);
            AssertConsistent(diagnostic.Span.End.Offset, diagnostic.Span.End.Line, diagnostic.Span.End.Column);
        }

        AssertConsistent(exception.Offset!.Value, exception.Line!.Value - 1, exception.Column!.Value - 1);

        void AssertConsistent(int offset, int line, int column)
        {
            var before = toml.AsSpan(0, Math.Min(offset, toml.Length));
            Assert.Equal(before.Count('\n'), line);
            Assert.Equal(before.Length - (before.LastIndexOf('\n') + 1), column);
        }
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
