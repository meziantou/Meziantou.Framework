extern alias TextDiffLib;

using Diff = TextDiffLib::Meziantou.Framework.TextDiff;
using TextDiffLib::Meziantou.Framework;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class TextDiffTests
{
    public static IEnumerable<object[]> AllAlgorithms()
    {
        foreach (var algorithm in Enum.GetValues<TextDiffAlgorithm>())
        {
            yield return new object[] { algorithm };
        }
    }

    [Fact]
    public void ComputeDiff_IdenticalTexts_NoDifferences()
    {
        var result = Diff.ComputeDiff("hello\nworld", "hello\nworld");

        Assert.False(result.HasDifferences);
        Assert.All(result.Entries, e => Assert.Equal(TextDiffOperation.Equal, e.Operation));
    }

    [Fact]
    public void ComputeDiff_EmptyTexts_NoDifferences()
    {
        var result = Diff.ComputeDiff("", "");

        Assert.False(result.HasDifferences);
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void ComputeDiff_AllAlgorithms_IdenticalTexts_NoDifferences(TextDiffAlgorithm algorithm)
    {
        var options = new TextDiffOptions { Algorithm = algorithm };
        var result = Diff.ComputeDiff("hello\nworld", "hello\nworld", options);

        Assert.False(result.HasDifferences);
        Assert.All(result.Entries, e => Assert.Equal(TextDiffOperation.Equal, e.Operation));
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void ComputeDiff_AllAlgorithms_InsertLine(TextDiffAlgorithm algorithm)
    {
        var options = new TextDiffOptions { Algorithm = algorithm };
        var result = Diff.ComputeDiff("line1\nline3", "line1\nline2\nline3", options);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Span.SequenceEqual("line2\n"));
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void ComputeDiff_AllAlgorithms_IgnoreOptions_NoDifferences(TextDiffAlgorithm algorithm)
    {
        var options = new TextDiffOptions
        {
            Algorithm = algorithm,
            IgnoreCase = true,
            IgnoreWhitespace = true,
            IgnoreEndOfLine = true,
        };

        var result = Diff.ComputeDiff("  HELLO  \r\n  WORLD  ", "hello\nworld", options);

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void ComputeDiff_InsertLine()
    {
        var result = Diff.ComputeDiff("line1\nline3", "line1\nline2\nline3");

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Span.SequenceEqual("line2\n"));
    }

    [Fact]
    public void ComputeDiff_DeleteLine()
    {
        var result = Diff.ComputeDiff("line1\nline2\nline3", "line1\nline3");

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Span.SequenceEqual("line2\n"));
    }

    [Fact]
    public void ComputeDiff_ModifyLine()
    {
        var result = Diff.ComputeDiff("line1\noriginal\nline3", "line1\nmodified\nline3");

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Span.SequenceEqual("original\n"));
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Span.SequenceEqual("modified\n"));
    }

    [Fact]
    public void ComputeDiff_CompletelyDifferent()
    {
        var result = Diff.ComputeDiff("aaa", "bbb");

        Assert.True(result.HasDifferences);
    }

    [Fact]
    public void ComputeDiff_OldEmpty_AllInserts()
    {
        var result = Diff.ComputeDiff("", "line1\nline2");

        Assert.True(result.HasDifferences);
        // The empty old text produces one empty chunk; new text produces inserts
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert);
    }

    [Fact]
    public void ComputeDiff_NewEmpty_AllDeletes()
    {
        var result = Diff.ComputeDiff("line1\nline2", "");

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete);
    }

    // Word-level tests
    [Fact]
    public void ComputeDiff_WordLevel_InsertWord()
    {
        var options = new TextDiffOptions { Chunker = TextChunker.Words };
        var result = Diff.ComputeDiff("hello world", "hello beautiful world", options);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Span.SequenceEqual("beautiful"));
    }

    [Fact]
    public void ComputeDiff_WordLevel_DeleteWord()
    {
        var options = new TextDiffOptions { Chunker = TextChunker.Words };
        var result = Diff.ComputeDiff("hello beautiful world", "hello world", options);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Span.SequenceEqual("beautiful"));
    }

    // Character-level tests
    [Fact]
    public void ComputeDiff_CharacterLevel_SingleChange()
    {
        var options = new TextDiffOptions { Chunker = TextChunker.Characters };
        var result = Diff.ComputeDiff("abc", "adc", options);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Span.SequenceEqual("b"));
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Span.SequenceEqual("d"));
    }

    [Fact]
    public void ComputeDiff_CharacterLevel_IdenticalTexts()
    {
        var options = new TextDiffOptions { Chunker = TextChunker.Characters };
        var result = Diff.ComputeDiff("abc", "abc", options);

        Assert.False(result.HasDifferences);
    }

    // IgnoreCase tests
    [Fact]
    public void ComputeDiff_IgnoreCase_NoDifferences()
    {
        var options = new TextDiffOptions { IgnoreCase = true };
        var result = Diff.ComputeDiff("Hello\nWorld", "hello\nworld", options);

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void ComputeDiff_CaseSensitive_HasDifferences()
    {
        var result = Diff.ComputeDiff("Hello\nWorld", "hello\nworld");

        Assert.True(result.HasDifferences);
    }

    // IgnoreWhitespace tests
    [Fact]
    public void ComputeDiff_IgnoreWhitespace_NoDifferences()
    {
        var options = new TextDiffOptions { IgnoreWhitespace = true };
        var result = Diff.ComputeDiff("  hello  \n  world  ", "hello\nworld", options);

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void ComputeDiff_WhitespaceSensitive_HasDifferences()
    {
        var result = Diff.ComputeDiff("  hello  \n  world  ", "hello\nworld");

        Assert.True(result.HasDifferences);
    }

    // IgnoreEndOfLine tests
    [Fact]
    public void ComputeDiff_IgnoreEndOfLine_CrLfVsLf()
    {
        var options = new TextDiffOptions { IgnoreEndOfLine = true };
        var result = Diff.ComputeDiff("line1\r\nline2", "line1\nline2", options);

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void ComputeDiff_IgnoreEndOfLine_CrVsLf()
    {
        var options = new TextDiffOptions { IgnoreEndOfLine = true };
        var result = Diff.ComputeDiff("line1\rline2", "line1\nline2", options);

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void ComputeDiff_IgnoreEndOfLine_UnicodeLineTerminators()
    {
        var options = new TextDiffOptions { IgnoreEndOfLine = true };
        var result = Diff.ComputeDiff("line1\u0085line2", "line1\nline2", options);
        Assert.False(result.HasDifferences);

        result = Diff.ComputeDiff("line1\u2028line2", "line1\nline2", options);
        Assert.False(result.HasDifferences);

        result = Diff.ComputeDiff("line1\u2029line2", "line1\nline2", options);
        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void ComputeDiff_EndOfLineSensitive_HasDifferences()
    {
        var result = Diff.ComputeDiff("line1\r\nline2", "line1\nline2");

        Assert.True(result.HasDifferences);
    }

    // Combined options
    [Fact]
    public void ComputeDiff_IgnoreCaseAndWhitespace()
    {
        var options = new TextDiffOptions { IgnoreCase = true, IgnoreWhitespace = true };
        var result = Diff.ComputeDiff("  HELLO  \n  WORLD  ", "hello\nworld", options);

        Assert.False(result.HasDifferences);
    }

    // TextDiffEntry equality
    [Fact]
    public void TextDiffEntry_Equality()
    {
        var a = new TextDiffEntry(TextDiffOperation.Equal, "hello".AsMemory());
        var b = new TextDiffEntry(TextDiffOperation.Equal, "hello".AsMemory());
        var c = new TextDiffEntry(TextDiffOperation.Insert, "hello".AsMemory());
        var d = new TextDiffEntry(TextDiffOperation.Equal, "world".AsMemory());

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
        Assert.True(a == b);
        Assert.True(a != c);
    }

    // Custom TextChunker
    [Fact]
    public void ComputeDiff_CustomChunker()
    {
        var options = new TextDiffOptions { Chunker = new SentenceChunker() };
        var result = Diff.ComputeDiff("Hello world. Goodbye world.", "Hello world. Hi world.", options);

        Assert.True(result.HasDifferences);
    }

    private sealed class SentenceChunker : TextChunker
    {
        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            var sentences = new List<string>();
            var start = 0;

            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '.')
                {
                    var end = i + 1;
                    while (end < value.Length && value[end] == ' ')
                    {
                        end++;
                    }

                    sentences.Add(value[start..end].ToString());
                    start = end;
                    i = end - 1;
                }
            }

            if (start < value.Length)
            {
                sentences.Add(value[start..].ToString());
            }

            return sentences;
        }
    }

    // Line chunker preserves line endings in chunks
    [Fact]
    public void LineChunker_PreservesLineEndings()
    {
        var chunks = TextChunker.Lines.Chunk("line1\nline2\r\nline3").ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Equal("line1\n", chunks[0]);
        Assert.Equal("line2\r\n", chunks[1]);
        Assert.Equal("line3", chunks[2]);
    }

    [Fact]
    public void LineChunker_UnicodeLineTerminators()
    {
        var chunks = TextChunker.Lines.Chunk("line1\u0085line2\u2028line3\u2029line4").ToList();

        Assert.Equal(4, chunks.Count);
        Assert.Equal("line1\u0085", chunks[0]);
        Assert.Equal("line2\u2028", chunks[1]);
        Assert.Equal("line3\u2029", chunks[2]);
        Assert.Equal("line4", chunks[3]);
    }

    // Word chunker tests
    [Fact]
    public void WordChunker_SplitsOnWhitespace()
    {
        var chunks = TextChunker.Words.Chunk("hello  world").ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Equal("hello", chunks[0]);
        Assert.Equal("  ", chunks[1]);
        Assert.Equal("world", chunks[2]);
    }

    // Character chunker tests
    [Fact]
    public void CharacterChunker_SplitsEachCharacter()
    {
        var chunks = TextChunker.Characters.Chunk("abc").ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Equal("a", chunks[0]);
        Assert.Equal("b", chunks[1]);
        Assert.Equal("c", chunks[2]);
    }
}
