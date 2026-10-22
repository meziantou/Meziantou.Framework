#pragma warning disable MA0004 // Use Task.ConfigureAwait
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;
using Meziantou.Framework.JavaScriptEngine.Intl;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Dictionaries;

/// <summary>
/// Builds dictionaries.bin (read by WordDictionaries): the word lists of ICU used by the word segmentation of
/// Intl.Segmenter (<c>data/brkitr/dictionaries/*dict.txt</c> of the ICU data release), one chunk per list in the order of
/// <see cref="WordDictionaryKind"/>. A list is read as <c>gendict</c> reads it and compiled into a trie (the format is
/// described by <see cref="WordDictionary"/>), with the options of the ICU build (<c>icu4c/source/data/BUILDRULES.py</c>).
/// </summary>
internal static class DictionaryGenerator
{
    // The lists, in the order of WordDictionaryKind, and the --transform offset of gendict (null for --uchars)
    private static readonly (WordDictionaryKind Kind, string File, int? CodePointBase)[] Dictionaries =
    [
        (WordDictionaryKind.ChineseJapanese, "cjdict.txt", null),
        (WordDictionaryKind.Thai, "thaidict.txt", 0x0E00),
        (WordDictionaryKind.Lao, "laodict.txt", 0x0E80),
        (WordDictionaryKind.Khmer, "khmerdict.txt", 0x1780),
        (WordDictionaryKind.Burmese, "burmesedict.txt", 0x1000),
    ];

    public static string GetIcuDataUrl(string version) => $"https://github.com/unicode-org/icu/releases/download/release-{version}/icu4c-{version}-data.zip";

    public static async Task<string> GetLatestIcuVersionAsync()
    {
        var json = await SharedHttpClient.InstanceWithAutoRedirect.GetStringAsync("https://api.github.com/repos/unicode-org/icu/releases/latest");
        var tag = (string)JsonNode.Parse(json)!["tag_name"]!;
        if (!tag.StartsWith("release-", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected tag of the latest ICU release: " + tag);

        return tag["release-".Length..];
    }

    public static async Task<int> RunAsync(FullPath root, JsonObject pins, FullPath downloadCache)
    {
        var version = (string)pins["icu"]!["version"]!;
        Console.WriteLine($"ICU {version}");
        var archive = await Downloads.DownloadCachedAsync(GetIcuDataUrl(version), (string)pins["icu"]!["sha256"]!, downloadCache);

        var chunks = new List<byte[]>();
        var wordLists = new List<SortedDictionary<string, int>>();
        using (var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
        {
            foreach (var (kind, file, codePointBase) in Dictionaries)
            {
                var entry = zip.GetEntry("data/brkitr/dictionaries/" + file) ?? throw new InvalidDataException("Missing file in the ICU data: " + file);
                string text;
                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    text = await reader.ReadToEndAsync();
                }

                var words = ReadWords(file, text, out var hasValues);
                chunks.Add(Compile(file, words, codePointBase, hasValues));
                wordLists.Add(words);
                Console.WriteLine($"{kind}: {words.Count} words, {chunks[^1].Length} bytes uncompressed");
            }
        }

        // The runtime must find every word with its value
        var content = ChunkedFileWriter.Write(chunks);
        var dictionaries = new WordDictionaries(CldrChunkedFile.FromBytes(content));
        var errors = 0;
        for (var i = 0; i < Dictionaries.Length; i++)
        {
            var dictionary = dictionaries.Get(Dictionaries[i].Kind);
            if (dictionary.WordCount != wordLists[i].Count)
            {
                Console.WriteLine($"{Dictionaries[i].File}: {dictionary.WordCount} words read back, expected {wordLists[i].Count}");
                errors++;
            }

            foreach (var (word, value) in wordLists[i])
            {
                if (!dictionary.TryGetValue(word, out var actual) || actual != value)
                {
                    Console.WriteLine($"{Dictionaries[i].File}: the word '{word}' is not read back");
                    errors++;
                }
            }
        }

        Console.WriteLine($"Verification: {errors} errors");
        if (errors > 0)
            return 3;

        var updated = false;
        var path = root / "src" / "Meziantou.Framework.JavaScriptEngine" / "Intl" / "Data" / "dictionaries.bin";
        if (!ChunkedFileWriter.IsSameContent(path, chunks))
        {
            await File.WriteAllBytesAsync(path, content);
            Console.WriteLine($"Updated {path} ({content.Length} bytes)");
            updated = true;
        }

        Console.WriteLine($"dictionaries.bin: {content.Length} bytes ({string.Join(", ", Dictionaries.Select((dictionary, index) => $"{dictionary.Kind} {dictionaries.File.GetCompressedChunkLength(index)}"))})");

        var versionFile = $$"""
            // <auto-generated/>
            // Generated by tools/Meziantou.Framework.JavaScriptEngine.Data.Generator from data-versions.json
            namespace Meziantou.Framework.JavaScriptEngine.Intl;

            internal sealed partial class WordDictionaries
            {
                /// <summary>The release of ICU whose word lists are embedded in dictionaries.bin.</summary>
                public const string EmbeddedIcuVersion = "{{version}}";
            }

            """;
        var versionFilePath = root / "src" / "Meziantou.Framework.JavaScriptEngine" / "Intl" / "Segmenter" / "WordDictionaries.g.cs";
        if (!File.Exists(versionFilePath) || (await File.ReadAllTextAsync(versionFilePath)).ReplaceLineEndings("\n") != versionFile)
        {
            await File.WriteAllTextAsync(versionFilePath, versionFile);
            updated = true;
        }

        return updated ? 1 : 0;
    }

    /// <summary>Reads a word list as gendict does (<c>icu4c/source/tools/gendict/gendict.cpp</c>): a word, then optionally white space and a value.</summary>
    private static SortedDictionary<string, int> ReadWords(string file, string text, out bool hasValues)
    {
        var words = new SortedDictionary<string, int>(StringComparer.Ordinal);
        hasValues = false;
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;

            // Strip the comment, the trailing CR, and the trailing spaces
            var line = rawLine.AsSpan();
            var comment = line.IndexOf('#');
            line = comment >= 0 ? line[..comment] : line.TrimEnd('\r');
            while (!line.IsEmpty && IsSpace(line[^1]))
            {
                line = line[..^1];
            }

            if (line.IsEmpty)
                continue;

            var keyLength = 0;
            while (keyLength < line.Length && !IsSpace(line[keyLength]))
            {
                keyLength++;
            }

            if (keyLength == 0)
                throw new InvalidDataException($"{file}:{lineNumber}: no word");

            var valueStart = keyLength;
            while (valueStart < line.Length && IsSpace(line[valueStart]))
            {
                valueStart++;
            }

            var value = 0;
            if (valueStart > keyLength)
            {
                value = ParseValue(file, lineNumber, line[valueStart..]);
                hasValues = true;
            }

            if (!words.TryAdd(line[..keyLength].ToString(), value))
                throw new InvalidDataException($"{file}:{lineNumber}: the word is already in the list");
        }

        return words;
    }

    // u_isspace
    private static bool IsSpace(char value) => value <= 0x9F
        ? value is ' ' or (>= '\t' and <= '\r') or (>= '\u001C' and <= '\u001F') or '\u0085'
        : char.GetUnicodeCategory(value) is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    // uprv_strtoul(s, &end, 0): decimal, octal with a leading 0, or hexadecimal with a leading 0x
    private static int ParseValue(string file, int lineNumber, ReadOnlySpan<char> value)
    {
        var digits = value;
        var radix = 10;
        if (value is ['0', 'x' or 'X', ..])
        {
            digits = value[2..];
            radix = 16;
        }
        else if (value is ['0', _, ..])
        {
            digits = value[1..];
            radix = 8;
        }

        long result = 0;
        foreach (var c in digits)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => int.MaxValue,
            };

            if (digit >= radix)
                throw new InvalidDataException($"{file}:{lineNumber}: invalid value '{value}'");

            result = (result * radix) + digit;
            if (result > byte.MaxValue)
                throw new InvalidDataException($"{file}:{lineNumber}: the value '{value}' does not fit in a byte");
        }

        if (digits.IsEmpty && radix != 8)
            throw new InvalidDataException($"{file}:{lineNumber}: invalid value '{value}'");

        return (int)result;
    }

    /// <summary>Compiles a word list into the format of <see cref="WordDictionary"/>.</summary>
    private static byte[] Compile(string file, SortedDictionary<string, int> words, int? codePointBase, bool hasValues)
    {
        // The letters of each word: the UTF-16 code units, or the bytes of gendict --transform (BytesDictionaryMatcher::transform)
        var entries = new List<(string Letters, int Value)>(words.Count);
        foreach (var (word, value) in words)
        {
            if (codePointBase is not { } offset)
            {
                entries.Add((word, value));
                continue;
            }

            var letters = new StringBuilder(word.Length);
            foreach (var rune in word.EnumerateRunes())
            {
                var letter = rune.Value switch
                {
                    0x200C => 0xFE,
                    0x200D => 0xFF,
                    _ => rune.Value - offset is >= 0 and <= 0xFD and var delta ? delta : throw new InvalidDataException($"{file}: the word '{word}' has a character outside of the block of its script (U+{rune.Value:X4})"),
                };

                letters.Append((char)letter);
            }

            entries.Add((letters.ToString(), value));
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Letters, b.Letters));

        // The trie in breadth-first order: a node is the words that share a prefix, a range of the sorted list
        var childCounts = new List<int>();
        var letterList = new List<char> { '\0' };
        var wordValues = new List<int?>();
        var nodes = new Queue<(int Start, int End, int Depth)>();
        nodes.Enqueue((0, entries.Count, 0));
        while (nodes.TryDequeue(out var node))
        {
            var start = node.Start;
            if (start < node.End && entries[start].Letters.Length == node.Depth)
            {
                wordValues.Add(entries[start].Value);
                start++;
            }
            else
            {
                wordValues.Add(null);
            }

            var childCount = 0;
            while (start < node.End)
            {
                var letter = entries[start].Letters[node.Depth];
                var end = start + 1;
                while (end < node.End && entries[end].Letters[node.Depth] == letter)
                {
                    end++;
                }

                letterList.Add(letter);
                nodes.Enqueue((start, end, node.Depth + 1));
                childCount++;
                start = end;
            }

            childCounts.Add(childCount);
        }

        var output = new BinaryBuffer();
        output.WriteByte(WordDictionary.FormatVersion);
        output.WriteByte((byte)((codePointBase is null ? 0 : WordDictionary.ByteLettersFlag) | (hasValues ? WordDictionary.HasValuesFlag : 0)));
        output.WriteBytes([0, 0]);
        output.WriteUInt32((uint)(codePointBase ?? 0));
        output.WriteUInt32((uint)childCounts.Count);
        foreach (var count in childCounts)
        {
            output.WriteVarUInt(count);
        }

        Span<byte> letterBytes = stackalloc byte[2];
        foreach (var letter in letterList)
        {
            if (codePointBase is null)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(letterBytes, letter);
                output.WriteBytes(letterBytes);
            }
            else
            {
                output.WriteByte((byte)letter);
            }
        }

        var isWord = new byte[(wordValues.Count + 7) / 8];
        for (var i = 0; i < wordValues.Count; i++)
        {
            if (wordValues[i] is not null)
            {
                isWord[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        output.WriteBytes(isWord);
        if (hasValues)
        {
            foreach (var value in wordValues)
            {
                if (value is { } wordValue)
                {
                    output.WriteByte((byte)wordValue);
                }
            }
        }

        return output.ToArray();
    }
}
