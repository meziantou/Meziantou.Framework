using System.Text;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Benchmarks;

[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
public class TextDiffBenchmarks
{
    private static readonly TextDiffOptions IgnoreWhitespaceAndCaseOptions = new() { IgnoreWhitespace = true, IgnoreCase = true };
    private static readonly TextDiffOptions DetectMovesOptions = new() { DetectMoves = true };
    private static readonly TextDiffOptions WordsOptions = new() { Chunker = TextChunker.Words };
    private static readonly TextDiffOptions CharactersOptions = new() { Chunker = TextChunker.Characters };
    private static readonly TextChunker[] LinesThenWords = [TextChunker.Lines, TextChunker.Words];

    private TextDiffOptions _options = new();
    private string _oldText = "";
    private string _newText = "";
    private string _theirsText = "";
    private string _oldProse = "";
    private string _newProse = "";
    private string _oldShortProse = "";
    private string _newShortProse = "";
    private string _patch = "";

    [Params(TextDiffAlgorithm.Myers, TextDiffAlgorithm.Patience, TextDiffAlgorithm.Histogram, TextDiffAlgorithm.HuntSzymanski)]
    public TextDiffAlgorithm Algorithm { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _options = new TextDiffOptions { Algorithm = Algorithm };

        // A source file of 5,000 lines, then a copy with about 1% of its lines changed, as in a typical commit
        var random = new Random(42);
        var lines = CreateSourceLines(random, 5000);
        _oldText = string.Join('\n', lines) + '\n';
        _newText = string.Join('\n', Edit(random, lines, editCount: 25)) + '\n';
        _theirsText = string.Join('\n', Edit(random, lines, editCount: 25)) + '\n';
        _patch = UnifiedDiff.Create(_oldText, _newText).ToString();

        // A text of about 2,000 words, as in a document, with a few words changed
        var words = CreateWords(random, 2000);
        _oldProse = string.Join(' ', words);
        _newProse = string.Join(' ', Edit(random, words, editCount: 20));

        var shortWords = words.Take(200).ToList();
        _oldShortProse = string.Join(' ', shortWords);
        _newShortProse = string.Join(' ', Edit(random, shortWords, editCount: 5));
    }

    [Benchmark]
    public TextDiffResult Lines() => TextDiff.ComputeDiff(_oldText, _newText, _options);

    [Benchmark]
    public TextDiffResult LinesIdentical() => TextDiff.ComputeDiff(_oldText, _oldText, _options);

    [Benchmark]
    public TextDiffResult LinesIgnoreWhitespaceAndCase()
    {
        IgnoreWhitespaceAndCaseOptions.Algorithm = Algorithm;
        return TextDiff.ComputeDiff(_oldText, _newText, IgnoreWhitespaceAndCaseOptions);
    }

    [Benchmark]
    public TextDiffResult LinesDetectMoves()
    {
        DetectMovesOptions.Algorithm = Algorithm;
        return TextDiff.ComputeDiff(_oldText, _newText, DetectMovesOptions);
    }

    [Benchmark]
    public TextDiffResult Words()
    {
        WordsOptions.Algorithm = Algorithm;
        return TextDiff.ComputeDiff(_oldProse, _newProse, WordsOptions);
    }

    [Benchmark]
    public TextDiffResult Characters()
    {
        CharactersOptions.Algorithm = Algorithm;
        return TextDiff.ComputeDiff(_oldShortProse, _newShortProse, CharactersOptions);
    }

    [Benchmark]
    public TextDiffHierarchyResult Hierarchy() => TextDiff.ComputeHierarchyDiff(_oldText, _newText, LinesThenWords, _options);

    [Benchmark]
    public UnifiedDiffFile Unified() => UnifiedDiff.Create(_oldText, _newText, new UnifiedDiffOptions { Algorithm = Algorithm });

    [Benchmark]
    public string UnifiedApply() => UnifiedDiff.Apply(_oldText, _patch);

    [Benchmark]
    public TextMergeResult Merge() => TextMerge.Merge(_oldText, _newText, _theirsText, new TextMergeOptions { DiffOptions = _options });

    private static List<string> CreateSourceLines(Random random, int count)
    {
        string[] identifiers = ["value", "result", "count", "index", "options", "builder", "buffer", "items", "name", "text"];
        string[] types = ["int", "string", "bool", "List<string>", "StringBuilder", "ReadOnlySpan<char>", "var"];
        var lines = new List<string>(count);
        var indent = 4;
        while (lines.Count < count)
        {
            var kind = random.Next(12);
            var padding = new string(' ', indent);
            switch (kind)
            {
                case 0:
                    lines.Add("");
                    break;
                case 1:
                    lines.Add(padding + "{");
                    indent = Math.Min(indent + 4, 16);
                    break;
                case 2:
                    indent = Math.Max(indent - 4, 4);
                    lines.Add(new string(' ', indent) + "}");
                    break;
                case 3:
                    lines.Add(padding + "return " + identifiers[random.Next(identifiers.Length)] + ";");
                    break;
                case 4:
                    lines.Add(padding + "// " + string.Join(' ', CreateWords(random, random.Next(3, 12))));
                    break;
                default:
                    lines.Add(padding + types[random.Next(types.Length)] + " " + identifiers[random.Next(identifiers.Length)] + random.Next(1000) + " = " + identifiers[random.Next(identifiers.Length)] + ".Method" + random.Next(100) + "(" + random.Next(10000) + ");");
                    break;
            }
        }

        return lines;
    }

    private static List<string> CreateWords(Random random, int count)
    {
        string[] vocabulary = ["the", "a", "diff", "text", "line", "word", "change", "file", "of", "to", "and", "in", "is", "that", "for", "it", "with", "as", "was", "on", "algorithm", "patience", "histogram", "merge", "conflict", "chunk", "result"];
        var words = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            words.Add(random.Next(4) is 0 ? vocabulary[random.Next(vocabulary.Length)] + random.Next(100) : vocabulary[random.Next(vocabulary.Length)]);
        }

        return words;
    }

    private static List<string> Edit(Random random, List<string> values, int editCount)
    {
        var result = new List<string>(values);
        for (var i = 0; i < editCount; i++)
        {
            var position = random.Next(result.Count - 4);
            switch (random.Next(3))
            {
                case 0:
                    result[position] += " // changed";
                    break;
                case 1:
                    result.Insert(position, "inserted" + random.Next(1000));
                    break;
                default:
                    result.RemoveRange(position, random.Next(1, 4));
                    break;
            }
        }

        return result;
    }
}
