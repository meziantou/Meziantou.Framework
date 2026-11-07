using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework;

namespace UnicodeDictionariesGenerator;

/// <summary>
/// Compiles the word lists of ICU (<c>data/brkitr/dictionaries/*dict.txt</c> of the ICU data release) into the format read
/// by <see cref="IcuWordDictionary"/>. A list is read as <c>gendict</c> reads it and compiled into a trie, with the options
/// of the ICU build (<c>icu4c/source/data/BUILDRULES.py</c>).
/// </summary>
internal static class DictionaryCompiler
{
    /// <summary>Gets the <c>--transform offset</c> of gendict for a list (null for <c>--uchars</c>).</summary>
    public static int? GetCodePointBase(IcuWordDictionaryKind kind) => kind switch
    {
        IcuWordDictionaryKind.ChineseJapanese => null,
        IcuWordDictionaryKind.Thai => 0x0E00,
        IcuWordDictionaryKind.Lao => 0x0E80,
        IcuWordDictionaryKind.Khmer => 0x1780,
        IcuWordDictionaryKind.Burmese => 0x1000,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Reads a word list as gendict does (<c>icu4c/source/tools/gendict/gendict.cpp</c>): a word, then optionally white space and a value.</summary>
    public static SortedDictionary<string, int> ReadWords(string file, string text, out bool hasValues)
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

    /// <summary>Compiles a word list into the format of <see cref="IcuWordDictionary"/>.</summary>
    public static byte[] Compile(string file, SortedDictionary<string, int> words, int? codePointBase, bool hasValues)
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

        using var output = new MemoryStream();
        Span<byte> buffer = stackalloc byte[4];
        output.WriteByte(IcuWordDictionary.FormatVersion);
        output.WriteByte((byte)((codePointBase is null ? 0 : IcuWordDictionary.ByteLettersFlag) | (hasValues ? IcuWordDictionary.HasValuesFlag : 0)));
        output.WriteByte(0);
        output.WriteByte(0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, codePointBase ?? 0);
        output.Write(buffer);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, childCounts.Count);
        output.Write(buffer);
        foreach (var count in childCounts)
        {
            WriteVarUInt(output, count);
        }

        foreach (var letter in letterList)
        {
            if (codePointBase is null)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, letter);
                output.Write(buffer[..2]);
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

        output.Write(isWord);
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

    private static void WriteVarUInt(Stream output, int value)
    {
        var remaining = checked((uint)value);
        while (remaining >= 0x80)
        {
            output.WriteByte((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        output.WriteByte((byte)remaining);
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
}
