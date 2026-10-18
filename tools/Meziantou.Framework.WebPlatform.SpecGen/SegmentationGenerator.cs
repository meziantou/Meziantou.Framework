using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>
/// Compiles the data of the dictionary-based line breaking of ICU (https://github.com/unicode-org/icu, the line breaking of
/// Chrome): the word lists of the Southeast Asian scripts (<c>icu4c/source/data/brkitr/dictionaries/*dict.txt</c>), used by
/// <c>SoutheastAsianWordBreaker</c> for the lexical word breaking of CSS Text (https://drafts.csswg.org/css-text-4/#lexical-breaking),
/// and the model of the Japanese phrase breaking (<c>icu4c/source/data/brkitr/adaboost/jaml.txt</c>, a model of BudouX), used by
/// <c>JapanesePhraseBreaker</c> for <c>word-break: auto-phrase</c> and <c>word-space-transform: auto-phrase</c>.
/// </summary>
/// <remarks>
/// <para>
/// A word list is compiled into a trie (format of <c>WordDictionary</c>, little-endian, compressed with Brotli by
/// <see cref="GeneratorContext.WriteCompressedFile"/>): version (u8), reserved (u8 × 3), the code point of the first character of
/// the block of the script (i32), node count N (i32); the trie in breadth-first order, so that the children of a node are
/// consecutive and sorted by letter: the child count of each node (N × u8), the letter of the edge to each node (N × u8, 0 for the
/// root), and a bit per node, set when the node ends a word (⌈N / 8⌉ bytes). As in the dictionaries of ICU, a letter is the offset
/// of the character from the start of the block (0 to 0xFD), U+200C is 0xFE and U+200D is 0xFF.
/// </para>
/// <para>
/// The model is a table of the scores of the features (one, two or three characters at a position around the boundary): each
/// key packs its code points in 21 bits each (see <c>JapanesePhraseBreaker</c>).
/// </para>
/// </remarks>
internal static partial class SegmentationGenerator
{
    private const byte FormatVersion = 1;

    /// <summary>The word lists: the name of the resource, the source file and the first code point of the block of the script.</summary>
    private static readonly (string Name, string File, int Base)[] Dictionaries =
    [
        ("burmese", "burmesedict.txt", 0x1000),
        ("khmer", "khmerdict.txt", 0x1780),
        ("lao", "laodict.txt", 0x0E80),
        ("thai", "thaidict.txt", 0x0E00),
    ];

    /// <summary>The features of the model, in the order of <c>JapanesePhraseBreaker</c> (the order of the <c>ModelIndex</c> of ICU).</summary>
    private static readonly string[] Features = ["UW1", "UW2", "UW3", "UW4", "UW5", "UW6", "BW1", "BW2", "BW3", "TW1", "TW2", "TW3", "TW4"];

    public static void Generate(GeneratorContext context)
    {
        var dataDirectory = context.SpecData("icu", "icu4c", "source", "data", "brkitr");
        var outputDirectory = context.Source("Unicode", "Segmentation");
        var notices = new StringBuilder();
        notices.Append("The word lists of this folder (*.dict) and the model of the Japanese phrase breaking (JapanesePhraseBreaker.Model.g.cs)\n");
        notices.Append("are compiled by tools/Meziantou.Framework.WebPlatform.SpecGen (SegmentationGenerator) from the data of ICU (https://github.com/unicode-org/icu):\n");
        notices.Append("icu4c/source/data/brkitr/dictionaries/*dict.txt and icu4c/source/data/brkitr/adaboost/jaml.txt. The data has been\n");
        notices.Append("modified: the word lists are converted into a binary trie, the model into tables. The code of SoutheastAsianWordBreaker\n");
        notices.Append("and JapanesePhraseBreaker is a port of the dictionary break engines of ICU (icu4c/source/common/dictbe.cpp, mlbe.cpp).\n");
        notices.Append("The license of ICU, and the headers of the source files with their copyright and license notices, follow.\n");
        notices.Append('\n').Append('=', 100).Append('\n');
        notices.Append("ICU: LICENSE\n");
        notices.Append('=', 100).Append('\n');
        notices.Append(ReadIcuLicense(context.SpecData("icu", "LICENSE")));
        foreach (var (name, file, codePointBase) in Dictionaries)
        {
            var path = Path.Combine(dataDirectory, "dictionaries", file);
            context.WriteCompressedFile(Path.Combine(outputDirectory, name + ".dict"), Compile(file, ReadWords(path), codePointBase));
            AppendHeader(notices, file, path);
        }

        var modelPath = Path.Combine(dataDirectory, "adaboost", "jaml.txt");
        AppendHeader(notices, "jaml.txt", modelPath);
        context.WriteFile(Path.Combine(outputDirectory, "LICENSES.txt"), notices.ToString());
        context.WriteFile(Path.Combine(outputDirectory, "JapanesePhraseBreaker.Model.g.cs"), GenerateModel(File.ReadAllText(modelPath)));
    }

    /// <summary>The license of ICU: the Unicode License v3, without the notices of the third-party software that is not used.</summary>
    private static string ReadIcuLicense(string path)
    {
        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        var sections = text.Split("\n----------------------------------------------------------------------\n");
        var result = new StringBuilder(sections[0].TrimEnd()).Append('\n');
        foreach (var section in sections.Skip(1))
        {
            // The notices of the word lists of Lao and Burmese, which are distributed with the library
            var title = section.TrimStart('\n').Split('\n')[0];
            if (title.Contains("Lao Word Break Dictionary Data", StringComparison.Ordinal) || title.Contains("Burmese Word Break Dictionary Data", StringComparison.Ordinal))
            {
                result.Append("\n----------------------------------------------------------------------\n").Append(section.TrimEnd()).Append('\n');
            }
        }

        return result.ToString();
    }

    private static void AppendHeader(StringBuilder notices, string name, string path)
    {
        notices.Append('\n').Append('=', 100).Append('\n');
        notices.Append(CultureInfo.InvariantCulture, $"{name}\n");
        notices.Append('=', 100).Append('\n');
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.TrimStart((char)0xFEFF, ' ', '\t');
            if (!trimmed.StartsWith('#', StringComparison.Ordinal) && !trimmed.StartsWith("//", StringComparison.Ordinal))
                break;

            notices.Append(line.TrimStart((char)0xFEFF).TrimEnd()).Append('\n');
        }
    }

    private static SortedSet<string> ReadWords(string path)
    {
        var words = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var word = line.TrimStart((char)0xFEFF).Trim();
            if (word.Length == 0 || word.StartsWith('#', StringComparison.Ordinal))
                continue;

            // (a word may be followed by a value: the lists of the Southeast Asian scripts have none)
            var tab = word.IndexOf('\t', StringComparison.Ordinal);
            words.Add(tab >= 0 ? word[..tab] : word);
        }

        return words;
    }

    private sealed class TrieNode
    {
        public SortedDictionary<byte, TrieNode> Children { get; } = [];

        public bool IsWord { get; set; }
    }

    private static byte[] Compile(string source, IEnumerable<string> words, int codePointBase)
    {
        var root = new TrieNode();
        foreach (var word in words)
        {
            var node = root;
            foreach (var c in word)
            {
                // The transformation of the dictionaries of ICU (BytesDictionaryMatcher::transform)
                var letter = c switch
                {
                    (char)0x200C => 0xFE,
                    (char)0x200D => 0xFF,
                    _ => c - codePointBase is >= 0 and <= 0xFD and var offset ? offset : throw new InvalidOperationException($"{source}: the word '{word}' has a character outside of the block of its script (U+{(int)c:X4})"),
                };

                if (!node.Children.TryGetValue((byte)letter, out var child))
                {
                    child = new TrieNode();
                    node.Children.Add((byte)letter, child);
                }

                node = child;
            }

            node.IsWord = true;
        }

        var nodes = new List<(TrieNode Node, byte Letter)> { (root, 0) };
        for (var i = 0; i < nodes.Count; i++)
        {
            foreach (var (letter, child) in nodes[i].Node.Children)
            {
                nodes.Add((child, letter));
            }
        }

        var output = new MemoryStream();
        var header = new byte[12];
        header[0] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), codePointBase);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), nodes.Count);
        output.Write(header);
        foreach (var (node, _) in nodes)
        {
            output.WriteByte(checked((byte)node.Children.Count));
        }

        foreach (var (_, letter) in nodes)
        {
            output.WriteByte(letter);
        }

        var wordEnds = new byte[(nodes.Count + 7) / 8];
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Node.IsWord)
            {
                wordEnds[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        output.Write(wordEnds);
        return output.ToArray();
    }

    private static string GenerateModel(string text)
    {
        // jaml.txt is an ICU resource bundle: for each feature, an array of strings (<feature>Keys) and an int vector (<feature>Values)
        var keys = new List<ulong>();
        var values = new List<int>();
        var starts = new List<int>();
        foreach (var feature in Features)
        {
            var keyMatch = Regex.Match(text, feature + @"Keys\s*\{(?<body>[^}]*)\}");
            var valueMatch = Regex.Match(text, feature + @"Values:intvector\s*\{(?<body>[^}]*)\}");
            if (!keyMatch.Success || !valueMatch.Success)
                throw new InvalidOperationException($"jaml.txt: the feature {feature} is missing");

            var featureKeys = KeyRegex().Matches(keyMatch.Groups["body"].Value).Select(m => m.Groups["key"].Value).ToArray();
            var featureValues = valueMatch.Groups["body"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (featureKeys.Length != featureValues.Length)
                throw new InvalidOperationException($"jaml.txt: the feature {feature} has {featureKeys.Length} keys and {featureValues.Length} values");

            var entries = new SortedDictionary<ulong, int>();
            for (var i = 0; i < featureKeys.Length; i++)
            {
                if (featureKeys[i].Contains('\\', StringComparison.Ordinal))
                    throw new InvalidOperationException($"jaml.txt: the key '{featureKeys[i]}' has an escape sequence");

                entries.Add(PackKey(featureKeys[i]), featureValues[i]);
            }

            starts.Add(keys.Count);
            keys.AddRange(entries.Keys);
            values.AddRange(entries.Values);
        }

        starts.Add(keys.Count);
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/icu (icu4c/source/data/brkitr/adaboost/jaml.txt)");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();
        using (writer.Block("internal static partial class JapanesePhraseBreaker"))
        {
            writer.Summary("The start of the features in <see cref=\"Keys\"/> and <see cref=\"Values\"/>: UW1 to UW6, BW1 to BW3 and TW1 to TW4, then the end.");
            writer.Line("private static ReadOnlySpan<int> FeatureStarts => [" + string.Join(", ", starts.Select(s => s.ToString(CultureInfo.InvariantCulture))) + "];");
            writer.Line();
            writer.Summary("The keys of the features (the code points of one to three characters, 21 bits each), sorted in each feature.");
            writer.Line("private static ReadOnlySpan<ulong> Keys =>");
            writer.Line("[");
            writer.Numbers(keys, perLine: 8);
            writer.Line("];");
            writer.Line();
            writer.Summary("The scores of the keys of <see cref=\"Keys\"/>.");
            writer.Line("private static ReadOnlySpan<int> Values =>");
            writer.Line("[");
            writer.Numbers(values, perLine: 16);
            writer.Line("];");
        }

        return writer.ToString();
    }

    /// <summary>Packs the code points of a key (one to three characters): the first one in the highest bits.</summary>
    private static ulong PackKey(string key)
    {
        ulong result = 0;
        var count = 0;
        foreach (var rune in key.EnumerateRunes())
        {
            result = (result << 21) | (uint)rune.Value;
            count++;
        }

        if (count is < 1 or > 3)
            throw new InvalidOperationException($"jaml.txt: the key '{key}' does not have one to three characters");

        return result;
    }

    [GeneratedRegex("\"(?<key>[^\"]*)\"")]
    private static partial Regex KeyRegex();
}
