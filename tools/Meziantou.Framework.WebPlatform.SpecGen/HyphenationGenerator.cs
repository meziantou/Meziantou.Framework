using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace SpecGen;

/// <summary>
/// Compiles the hyphenation patterns of hyph-utf8 (https://github.com/hyphenation/tex-hyphen, the source of the dictionaries
/// of Chrome on Android, Linux and ChromeOS and of Firefox) into the dictionaries of <c>HyphenationDictionary</c>, the automatic
/// hyphenation of <c>hyphens: auto</c> (https://drafts.csswg.org/css-text-4/#hyphenation).
/// </summary>
/// <remarks>
/// <para>
/// The patterns (<c>hyph-*.pat.txt</c>) and the exceptions (<c>hyph-*.hyp.txt</c>) of a language are compiled into a packed trie
/// (Liang's algorithm, https://tug.org/docs/liang/): the exceptions are whole-word patterns (<c>.a10s11s10o11c10i10a10t10e.</c>) whose
/// odd and even values override the values of the patterns (digits). The letters are numbered (0 is the word boundary <c>.</c>), and
/// the alphabet maps each letter and the characters whose simple lowercase mapping is a letter to its number.
/// </para>
/// <para>
/// Format (little-endian, compressed with Brotli by <see cref="GeneratorContext.WriteCompressedFile"/>): version (u8), reserved
/// (u8), alphabet length A (u16), node count N (i32), value pool length V (u16), reserved (u16); the code points of the
/// alphabet (A × u16, sorted) and their letters (A × u8); the trie in breadth-first order, so that the children of a node are
/// consecutive and sorted by letter: the child count of each node (N × u8), the letter of the edge to each node (N × u8, 0 for
/// the root), and the offset of the values of each node in the pool (N × u16, 0 for none); the value pool (V × u8): for each
/// value, the position of its first non-zero digit, the number of digits, and the digits.
/// </para>
/// </remarks>
internal static class HyphenationGenerator
{
    private const byte FormatVersion = 1;

    /// <summary>
    /// The dictionaries: the name (the locale of the dictionary of Chrome, see <c>HyphenationDictionary</c>) and the source (the language of
    /// the hyph-utf8 files). Only the patterns under a license that allows their redistribution in this library are included:
    /// the patterns available only under the GPL, the LGPL or the MPL (cs, hu, hy, lv, sr-cyrl) are not. Serbian uses the
    /// Serbo-Croatian patterns (LPPL), whose Cyrillic and Latin variants hyphenate the same words.
    /// </summary>
    private static readonly (string Name, string Source)[] Dictionaries =
    [
        ("af", "af"), ("as", "as"), ("be", "be"), ("bg", "bg"), ("bn", "bn"), ("cy", "cy"), ("da", "da"), ("de-1996", "de-1996"),
        ("de-ch-1901", "de-ch-1901"), ("el", "el-monoton"), ("en-gb", "en-gb"), ("en-us", "en-us"), ("es", "es"), ("et", "et"),
        ("eu", "eu"), ("fr", "fr"), ("ga", "ga"), ("gl", "gl"), ("gu", "gu"), ("hi", "hi"), ("hr", "hr"), ("it", "it"), ("ka", "ka"),
        ("kn", "kn"), ("la", "la"), ("lt", "lt"), ("ml", "ml"), ("mn-cyrl", "mn-cyrl"), ("mr", "mr"), ("nb", "nb"), ("nl", "nl"),
        ("nn", "nn"), ("or", "or"), ("pa", "pa"), ("pt", "pt"), ("ru", "ru"), ("sk", "sk"), ("sl", "sl"), ("sq", "sq"),
        ("sr-cyrl", "sh-cyrl"), ("sr-latn", "sh-latn"), ("sv", "sv"), ("ta", "ta"), ("te", "te"), ("tk", "tk"), ("uk", "uk"),
    ];

    public static void Generate(GeneratorContext context)
    {
        var patternsDirectory = context.SpecData("hyphenation", "hyph-utf8", "tex", "generic", "hyph-utf8", "patterns");
        var lowercaseMappings = LoadLowercaseMappings(context.SpecData("unicode", "UnicodeData.txt"));
        var outputDirectory = context.Source("Unicode", "Hyphenation");

        // The dictionaries whose compiled data is identical (e.g. nb and nn) share their resource
        var resources = new List<(string Name, byte[] Data)>();
        var resourceNames = new List<string>();
        var notices = new StringBuilder();
        notices.Append("The hyphenation dictionaries of this folder (*.hyph) are compiled by tools/Meziantou.Framework.WebPlatform.SpecGen (HyphenationGenerator) from the\n");
        notices.Append("hyphenation patterns of hyph-utf8 (https://github.com/hyphenation/tex-hyphen). The data has been modified: the\n");
        notices.Append("patterns and the exceptions are converted into a binary trie. The headers of the source files, with their copyright\n");
        notices.Append("and license notices, follow.\n");
        foreach (var (name, source) in Dictionaries)
        {
            var patterns = File.ReadAllText(Path.Combine(patternsDirectory, "txt", $"hyph-{source}.pat.txt"));
            var exceptionsPath = Path.Combine(patternsDirectory, "txt", $"hyph-{source}.hyp.txt");
            var exceptions = File.Exists(exceptionsPath) ? File.ReadAllText(exceptionsPath) : "";
            var data = Compile(source, patterns, exceptions, lowercaseMappings);
            var existing = resources.FindIndex(resource => resource.Data.AsSpan().SequenceEqual(data));
            if (existing >= 0)
            {
                resourceNames.Add(resources[existing].Name);
            }
            else
            {
                resources.Add((name, data));
                resourceNames.Add(name);
            }

            notices.Append('\n').Append('=', 100).Append('\n');
            notices.Append(CultureInfo.InvariantCulture, $"{name}: hyph-{source}.tex\n");
            notices.Append('=', 100).Append('\n');
            foreach (var line in File.ReadLines(Path.Combine(patternsDirectory, "tex", $"hyph-{source}.tex")))
            {
                if (!line.StartsWith('%', StringComparison.Ordinal))
                    break;

                notices.Append(line.TrimEnd()).Append('\n');
            }
        }

        foreach (var (name, data) in resources)
        {
            context.WriteCompressedFile(Path.Combine(outputDirectory, name + ".hyph"), data);
        }

        foreach (var path in Directory.Exists(outputDirectory) ? Directory.GetFiles(outputDirectory, "*.hyph") : [])
        {
            if (!resources.Exists(resource => resource.Name == Path.GetFileNameWithoutExtension(path)))
            {
                context.DeleteFile(path);
            }
        }

        context.WriteFile(Path.Combine(outputDirectory, "LICENSES.txt"), notices.ToString());

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/hyphenation (hyph-utf8)");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();
        using (writer.Block("internal sealed partial class HyphenationDictionary"))
        {
            writer.Summary("The names of the dictionaries: the locales of the dictionaries of Chrome (lowercase).");
            writer.Line("private static readonly string[] DictionaryNames = [" + string.Join(", ", Dictionaries.Select(d => CodeWriter.Literal(d.Name))) + "];");
            writer.Line();
            writer.Summary("The embedded resource of each dictionary of <see cref=\"DictionaryNames\"/> (<c>Unicode/Hyphenation/&lt;name&gt;.hyph</c>): the dictionaries with the same patterns share it.");
            writer.Line("private static readonly string[] DictionaryResources = [" + string.Join(", ", resourceNames.Select(CodeWriter.Literal)) + "];");
        }

        context.WriteFile(Path.Combine(outputDirectory, "HyphenationDictionary.Dictionaries.g.cs"), writer.ToString());
    }

    /// <summary>The simple lowercase mappings of the BMP (field 13 of UnicodeData.txt), used to match the uppercase letters.</summary>
    private static Dictionary<char, char> LoadLowercaseMappings(string path)
    {
        var mappings = new Dictionary<char, char>();
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split(';');
            if (fields.Length < 14 || fields[13].Length == 0)
                continue;

            var codePoint = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var lowercase = int.Parse(fields[13], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (codePoint <= 0xFFFF && lowercase <= 0xFFFF)
            {
                mappings[(char)codePoint] = (char)lowercase;
            }
        }

        return mappings;
    }

    private sealed class TrieNode
    {
        public SortedDictionary<byte, TrieNode> Children { get; } = [];

        public byte[]? Values { get; set; }
    }

    private static byte[] Compile(string source, string patternsText, string exceptionsText, Dictionary<char, char> lowercaseMappings)
    {
        // The letters of each pattern (with the word boundaries ".") and its values: values[i] is the digit before letter i
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pattern in patternsText.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var letters = new StringBuilder();
            var values = new List<byte> { 0 };
            foreach (var c in pattern)
            {
                if (char.IsAsciiDigit(c))
                {
                    values[^1] = (byte)(c - '0');
                }
                else
                {
                    letters.Append(c);
                    values.Add(0);
                }
            }

            Add(letters.ToString(), [.. values]);
        }

        // https://tug.org/TUGboat/tb09-1/tb20liang.pdf: the exceptions override the patterns. A whole-word pattern whose values are
        // 11 (a hyphen) and 10 (no hyphen) between its letters wins over the patterns, whose values are digits.
        var exceptions = exceptionsText.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var exception in exceptions)
        {
            var word = exception.Replace("-", "", StringComparison.Ordinal);
            var values = new byte[word.Length + 3];
            var index = 0;
            for (var i = 0; i < exception.Length; i++)
            {
                if (exception[i] == '-')
                    continue;

                if (index > 0)
                {
                    values[index + 1] = exception[i - 1] == '-' ? (byte)11 : (byte)10;
                }

                index++;
            }

            Add("." + word + ".", values);
        }

        // The alphabet: the letters of the patterns (the boundary "." is 0), and the characters whose lowercase is a letter
        var letterSet = new SortedSet<char>();
        foreach (var letters in entries.Keys)
        {
            foreach (var c in letters)
            {
                if (char.IsSurrogate(c))
                    throw new InvalidOperationException($"hyph-{source}: the patterns have characters outside of the BMP");

                if (c != '.')
                {
                    letterSet.Add(c);
                }
            }
        }

        if (letterSet.Count > 254)
            throw new InvalidOperationException($"hyph-{source}: too many letters ({letterSet.Count})");

        var codes = new Dictionary<char, byte> { ['.'] = 0 };
        foreach (var c in letterSet)
        {
            codes[c] = (byte)codes.Count;
        }

        var alphabet = new SortedDictionary<char, byte>();
        foreach (var c in letterSet)
        {
            alphabet[c] = codes[c];
        }

        foreach (var (c, lowercase) in lowercaseMappings)
        {
            if (!letterSet.Contains(c) && codes.TryGetValue(lowercase, out var code) && lowercase != '.')
            {
                alphabet[c] = code;
            }
        }

        // The trie, numbered in breadth-first order
        var root = new TrieNode();
        foreach (var (letters, values) in entries)
        {
            var node = root;
            foreach (var c in letters)
            {
                var code = codes[c];
                if (!node.Children.TryGetValue(code, out var child))
                {
                    child = new TrieNode();
                    node.Children.Add(code, child);
                }

                node = child;
            }

            node.Values = values;
        }

        var nodes = new List<(TrieNode Node, byte Code)> { (root, 0) };
        for (var i = 0; i < nodes.Count; i++)
        {
            foreach (var (code, child) in nodes[i].Node.Children)
            {
                nodes.Add((child, code));
            }
        }

        // The values, without the zeros before and after the digits, shared by the nodes
        var pool = new List<byte> { 0 };
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var valueOffsets = new ushort[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Node.Values is not { } values)
                continue;

            var first = Array.FindIndex(values, v => v != 0);
            if (first < 0)
                continue;

            var last = Array.FindLastIndex(values, v => v != 0);
            var digits = values.AsSpan(first, last - first + 1).ToArray();
            var key = first + ":" + Convert.ToHexString(digits);
            if (!offsets.TryGetValue(key, out var offset))
            {
                offset = pool.Count;
                offsets.Add(key, offset);
                pool.Add((byte)first);
                pool.Add((byte)digits.Length);
                pool.AddRange(digits);
            }

            valueOffsets[i] = checked((ushort)offset);
        }

        var output = new MemoryStream();
        var header = new byte[12];
        header[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), checked((ushort)alphabet.Count));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), nodes.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), checked((ushort)pool.Count));
        output.Write(header);
        var buffer = new byte[2];
        foreach (var c in alphabet.Keys)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, c);
            output.Write(buffer);
        }

        foreach (var code in alphabet.Values)
        {
            output.WriteByte(code);
        }

        foreach (var (node, _) in nodes)
        {
            output.WriteByte(checked((byte)node.Children.Count));
        }

        foreach (var (_, code) in nodes)
        {
            output.WriteByte(code);
        }

        foreach (var offset in valueOffsets)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, offset);
            output.Write(buffer);
        }

        output.Write(pool.ToArray());
        return output.ToArray();

        void Add(string letters, byte[] values)
        {
            if (entries.TryGetValue(letters, out var existing))
            {
                for (var i = 0; i < values.Length; i++)
                {
                    existing[i] = Math.Max(existing[i], values[i]);
                }
            }
            else
            {
                entries.Add(letters, values);
            }
        }
    }
}
