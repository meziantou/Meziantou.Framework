extern alias TextDiffLib;

using System.Text;

using Diff = TextDiffLib::Meziantou.Framework.TextDiff;
using TextDiffLib::Meziantou.Framework;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class TextDiffTests
{
    private static readonly IReadOnlyList<DiffCorpusCase> LineCorpus =
    [
        new("Identical text", JoinLines("alpha", "beta"), JoinLines("alpha", "beta"), HasDifferences: false),
        new("Insert in the middle", JoinLines("line1", "line3"), JoinLines("line1", "line2", "line3"), HasDifferences: true),
        new("Delete from the beginning", JoinLines("line1", "line2", "line3"), JoinLines("line3"), HasDifferences: true),
        new("Repeated anchors", JoinLines("A", "B", "A", "C"), JoinLines("A", "A", "B", "C"), HasDifferences: true),
        new("Different line endings", "line1\r\nline2\r\nline3", "line1\nline2\nline3", HasDifferences: true),
        new("GNU diffutils sample", JoinLines(
            "The Way that can be told of is not the eternal Way;",
            "The name that can be named is not the eternal name.",
            "The Nameless is the origin of Heaven and Earth;",
            "The Named is the mother of all things.",
            "Therefore let there always be non-being,",
            "  so we may see their subtlety,",
            "And let there always be being,",
            "  so we may see their outcome.",
            "The two are the same,",
            "But after they are produced,",
            "  they have different names."),
            JoinLines(
                "The Nameless is the origin of Heaven and Earth;",
                "The named is the mother of all things.",
                "",
                "Therefore let there always be non-being,",
                "  so we may see their subtlety,",
                "And let there always be being,",
                "  so we may see their outcome.",
                "The two are the same,",
                "But after they are produced,",
                "  they have different names.",
                "They both may be called deep and profound.",
                "Deeper and more profound,",
                "The door of all subtleties!"),
            HasDifferences: true),
    ];

    private static readonly IReadOnlyList<DiffCorpusCase> WordCorpus =
    [
        new("Insert adjective", "the quick fox", "the quick brown fox", HasDifferences: true),
        new("Delete word", "hello beautiful world", "hello world", HasDifferences: true),
        new("Identical", "same words", "same words", HasDifferences: false),
    ];

    private static readonly IReadOnlyList<DiffCorpusCase> CharacterCorpus =
    [
        new("Single character replacement", "abc", "adc", HasDifferences: true),
        new("Prefix insertion", "abc", "xabc", HasDifferences: true),
        new("Identical", "same", "same", HasDifferences: false),
    ];

    private static readonly IReadOnlyList<AlgorithmCase> AlgorithmSpecificCorpus =
    [
        new(
            "Myers repeated anchors",
            TextDiffAlgorithm.Myers,
            JoinLines("A", "B", "A", "C"),
            JoinLines("A", "A", "B", "C"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "A\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "B\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "A\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "C"),
            ]),
        new(
            "Patience repeated anchors",
            TextDiffAlgorithm.Patience,
            JoinLines("A", "B", "A", "C"),
            JoinLines("A", "A", "B", "C"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "B\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "B\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "C"),
            ]),
        new(
            "Histogram repeated anchors",
            TextDiffAlgorithm.Histogram,
            JoinLines("A", "B", "A", "C"),
            JoinLines("A", "A", "B", "C"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "A\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "B\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "A\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "C"),
            ]),
        new(
            "Hunt-Szymanski repeated anchors",
            TextDiffAlgorithm.HuntSzymanski,
            JoinLines("A", "B", "A", "C"),
            JoinLines("A", "A", "B", "C"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "B\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "B\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "C"),
            ]),
        new(
            "Myers swap middle lines",
            TextDiffAlgorithm.Myers,
            JoinLines("a", "b", "c", "d"),
            JoinLines("a", "c", "b", "d"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "a\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "c\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "b\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "c\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "d"),
            ]),
        new(
            "Patience swap middle lines",
            TextDiffAlgorithm.Patience,
            JoinLines("a", "b", "c", "d"),
            JoinLines("a", "c", "b", "d"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "a\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "b\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "c\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "b\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "d"),
            ]),
        new(
            "Histogram swap middle lines",
            TextDiffAlgorithm.Histogram,
            JoinLines("a", "b", "c", "d"),
            JoinLines("a", "c", "b", "d"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "a\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "c\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "b\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "c\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "d"),
            ]),
        new(
            "Hunt-Szymanski swap middle lines",
            TextDiffAlgorithm.HuntSzymanski,
            JoinLines("a", "b", "c", "d"),
            JoinLines("a", "c", "b", "d"),
            [
                new TextDiffEntry(TextDiffOperation.Equal, "a\n"),
                new TextDiffEntry(TextDiffOperation.Delete, "b\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "c\n"),
                new TextDiffEntry(TextDiffOperation.Insert, "b\n"),
                new TextDiffEntry(TextDiffOperation.Equal, "d"),
            ]),
    ];

    public static IEnumerable<object[]> AllAlgorithms()
    {
        foreach (var algorithm in Enum.GetValues<TextDiffAlgorithm>())
        {
            yield return new object[] { algorithm };
        }
    }

    public static IEnumerable<object[]> AllAlgorithmsLineCorpus()
    {
        foreach (var algorithm in Enum.GetValues<TextDiffAlgorithm>())
        {
            foreach (var testCase in LineCorpus)
            {
                yield return new object[] { algorithm, testCase.Name, testCase.OldText, testCase.NewText, testCase.HasDifferences };
            }
        }
    }

    public static IEnumerable<object[]> AlgorithmSpecificCases()
    {
        foreach (var testCase in AlgorithmSpecificCorpus)
        {
            yield return new object[] { testCase.Algorithm, testCase.Name, testCase.OldText, testCase.NewText, testCase.ExpectedEntries };
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
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Equals("line2\n", StringComparison.Ordinal));
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

    [Theory]
    [MemberData(nameof(AllAlgorithmsLineCorpus))]
    public void ComputeDiff_AllAlgorithms_LineCorpus_ReconstructsOldAndNewText(TextDiffAlgorithm algorithm, string _, string oldText, string newText, bool hasDifferences)
    {
        var options = new TextDiffOptions { Algorithm = algorithm };
        var result = Diff.ComputeDiff(oldText, newText, options);

        Assert.Equal(hasDifferences, result.HasDifferences);
        Assert.Equal(oldText, ReconstructOldText(result));
        Assert.Equal(newText, ReconstructNewText(result));
    }

    [Theory]
    [MemberData(nameof(AlgorithmSpecificCases))]
    public void ComputeDiff_AlgorithmSpecificCases_ProducesExpectedEntries(TextDiffAlgorithm algorithm, string _, string oldText, string newText, TextDiffEntry[] expectedEntries)
    {
        var options = new TextDiffOptions { Algorithm = algorithm };
        var result = Diff.ComputeDiff(oldText, newText, options);

        Assert.Equal(expectedEntries, result.Entries);
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void ComputeDiff_AllAlgorithms_WordCorpus_ReconstructsOldAndNewText(TextDiffAlgorithm algorithm)
    {
        foreach (var testCase in WordCorpus)
        {
            var options = new TextDiffOptions { Algorithm = algorithm, Chunker = TextChunker.Words };
            var result = Diff.ComputeDiff(testCase.OldText, testCase.NewText, options);

            Assert.Equal(testCase.HasDifferences, result.HasDifferences);
            Assert.Equal(testCase.OldText, ReconstructOldText(result));
            Assert.Equal(testCase.NewText, ReconstructNewText(result));
        }
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void ComputeDiff_AllAlgorithms_CharacterCorpus_ReconstructsOldAndNewText(TextDiffAlgorithm algorithm)
    {
        foreach (var testCase in CharacterCorpus)
        {
            var options = new TextDiffOptions { Algorithm = algorithm, Chunker = TextChunker.Characters };
            var result = Diff.ComputeDiff(testCase.OldText, testCase.NewText, options);

            Assert.Equal(testCase.HasDifferences, result.HasDifferences);
            Assert.Equal(testCase.OldText, ReconstructOldText(result));
            Assert.Equal(testCase.NewText, ReconstructNewText(result));
        }
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void ComputeDiff_AllAlgorithms_GnuDiffCorpus_ContainsExpectedChanges(TextDiffAlgorithm algorithm)
    {
        var testCase = LineCorpus.Single(c => c.Name == "GNU diffutils sample");
        var options = new TextDiffOptions { Algorithm = algorithm };
        var result = Diff.ComputeDiff(testCase.OldText, testCase.NewText, options);

        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Equals("The Way that can be told of is not the eternal Way;\n", StringComparison.Ordinal));
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Equals("The name that can be named is not the eternal name.\n", StringComparison.Ordinal));
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Equals("The named is the mother of all things.\n", StringComparison.Ordinal));
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Equals("The door of all subtleties!", StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeDiff_InsertLine()
    {
        var result = Diff.ComputeDiff("line1\nline3", "line1\nline2\nline3");

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Equals("line2\n", StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeDiff_DeleteLine()
    {
        var result = Diff.ComputeDiff("line1\nline2\nline3", "line1\nline3");

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Equals("line2\n", StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeDiff_ModifyLine()
    {
        var result = Diff.ComputeDiff("line1\noriginal\nline3", "line1\nmodified\nline3");

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Equals("original\n", StringComparison.Ordinal));
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Equals("modified\n", StringComparison.Ordinal));
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
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Equals("beautiful", StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeDiff_WordLevel_DeleteWord()
    {
        var options = new TextDiffOptions { Chunker = TextChunker.Words };
        var result = Diff.ComputeDiff("hello beautiful world", "hello world", options);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Equals("beautiful", StringComparison.Ordinal));
    }

    // Character-level tests
    [Fact]
    public void ComputeDiff_CharacterLevel_SingleChange()
    {
        var options = new TextDiffOptions { Chunker = TextChunker.Characters };
        var result = Diff.ComputeDiff("abc", "adc", options);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Delete && e.Text.Equals("b", StringComparison.Ordinal));
        Assert.Contains(result.Entries, e => e.Operation == TextDiffOperation.Insert && e.Text.Equals("d", StringComparison.Ordinal));
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
        var a = new TextDiffEntry(TextDiffOperation.Equal, "hello");
        var b = new TextDiffEntry(TextDiffOperation.Equal, "hello");
        var c = new TextDiffEntry(TextDiffOperation.Insert, "hello");
        var d = new TextDiffEntry(TextDiffOperation.Equal, "world");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void TextDiffResult_ToString_ContainsSummaryAndOperations()
    {
        var result = Diff.ComputeDiff("line1\nline2\n", "line1\nline3\n");

        var text = result.ToString();

        Assert.Equal("Insertions: 1, Deletions: 1, Equals: 2", text);
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

    private static string ReconstructOldText(TextDiffResult result)
    {
        var sb = new StringBuilder();
        foreach (var entry in result.Entries)
        {
            if (entry.Operation is TextDiffOperation.Equal or TextDiffOperation.Delete)
            {
                sb.Append(entry.Text);
            }
        }

        return sb.ToString();
    }

    private static string ReconstructNewText(TextDiffResult result)
    {
        var sb = new StringBuilder();
        foreach (var entry in result.Entries)
        {
            if (entry.Operation is TextDiffOperation.Equal or TextDiffOperation.Insert)
            {
                sb.Append(entry.Text);
            }
        }

        return sb.ToString();
    }

    private static string JoinLines(params string[] lines) => string.Join('\n', lines);

    private sealed record DiffCorpusCase(string Name, string OldText, string NewText, bool HasDifferences);
    private sealed record AlgorithmCase(string Name, TextDiffAlgorithm Algorithm, string OldText, string NewText, TextDiffEntry[] ExpectedEntries);
}
