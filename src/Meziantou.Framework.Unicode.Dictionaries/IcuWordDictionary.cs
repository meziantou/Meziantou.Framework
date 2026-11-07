using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework;

/// <summary>
/// A word list of ICU (<c>icu4c/source/data/brkitr/dictionaries/*dict.txt</c>), embedded in this package: the words of
/// Chinese and Japanese, Thai, Lao, Khmer or Burmese, which <see cref="IcuDictionaryBreakEngine"/> uses to find the words
/// of a text.
/// </summary>
/// <remarks>
/// <para>
/// A list is decompressed and loaded on the first call of <see cref="Get"/> for it, so an application only pays for the
/// lists of the scripts of its texts. In memory, a list takes from about 0.5 MB (Thai) to 3 MB (Chinese and Japanese). An
/// instance is immutable and can be used by several threads at the same time.
/// </para>
/// <para>
/// The lists are the ones of the release of ICU given by <see cref="IcuVersion"/>. They are distributed under the
/// Unicode License v3, and the lists of Chinese and Japanese, Lao and Burmese have additional notices: see the
/// THIRD-PARTY-NOTICES.md file of the package.
/// </para>
/// </remarks>
public sealed partial class IcuWordDictionary
{
    /// <summary>The version of the format of the data.</summary>
    internal const byte FormatVersion = 1;

    /// <summary>The letters are bytes: the offset of the characters from the code point base.</summary>
    internal const byte ByteLettersFlag = 1;

    /// <summary>The words have a value (the cost of the words of the Chinese and Japanese list).</summary>
    internal const byte HasValuesFlag = 2;

    private const string ResourcePrefix = "Meziantou.Framework.Unicode.Dictionaries.";
    private const int HeaderSize = 12;

    private static readonly Lazy<IcuWordDictionary>[] Dictionaries = CreateDictionaries();

    private readonly bool _byteLetters;
    private readonly int _codePointBase;

    // The index of the first child of each node, and of the node after the last one: the children of a node are the
    // nodes from _firstChild[node] to _firstChild[node + 1], excluded
    private readonly int[] _firstChild;
    private readonly char[] _letters;
    private readonly ulong[] _isWord;
    private readonly byte[]? _values;

    /// <summary>
    /// Reads a word list from its uncompressed content.
    /// </summary>
    /// <remarks>
    /// <para>The format, little endian, is:</para>
    /// <code>
    /// version:u8 flags:u8 reserved:u16 codePointBase:i32 nodeCount:i32
    /// childCount:varuint[nodeCount]              (the nodes in breadth-first order: the children of a node are consecutive and sorted by letter)
    /// letter:u8[nodeCount] or u16[nodeCount]     (the letter of the edge to each node, 0 for the root)
    /// isWord:u8[(nodeCount + 7) / 8]             (a bit per node, set when the node ends a word)
    /// value:u8[wordCount]                        (with HasValues: the value of each word, in the order of the nodes)
    /// </code>
    /// <para>
    /// As in the byte tries of ICU (<c>BytesDictionaryMatcher</c>, <c>gendict --bytes --transform offset-0x....</c>), a letter
    /// of a list with <see cref="ByteLettersFlag"/> is the offset of the character from <c>codePointBase</c> (0 to 0xFD), U+200C
    /// is 0xFE and U+200D is 0xFF. The other lists (<c>gendict --uchars</c>) have the UTF-16 code units of the words.
    /// </para>
    /// </remarks>
    private IcuWordDictionary(IcuWordDictionaryKind kind, ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || data[0] != FormatVersion)
            throw new InvalidDataException("The word list has an unsupported format");

        Kind = kind;
        var flags = data[1];
        _byteLetters = (flags & ByteLettersFlag) != 0;
        _codePointBase = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var nodeCount = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        if (nodeCount <= 0 || nodeCount > data.Length)
            throw new InvalidDataException("The word list is invalid");

        var offset = HeaderSize;
        _firstChild = new int[nodeCount + 1];
        var next = 1;
        for (var node = 0; node < nodeCount; node++)
        {
            _firstChild[node] = next;
            next += ReadVarUInt(data, ref offset);
            if (next > nodeCount)
                throw new InvalidDataException("The word list is invalid");
        }

        _firstChild[nodeCount] = next;
        if (next != nodeCount)
            throw new InvalidDataException("The word list is invalid");

        var letterSize = _byteLetters ? 1 : 2;
        if (data.Length - offset < (nodeCount * letterSize) + ((nodeCount + 7) / 8))
            throw new InvalidDataException("The word list is invalid");

        _letters = new char[nodeCount];
        for (var node = 0; node < nodeCount; node++)
        {
            if (_byteLetters)
            {
                _letters[node] = (char)data[offset];
                offset++;
            }
            else
            {
                _letters[node] = (char)BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
                offset += 2;
            }
        }

        _isWord = new ulong[(nodeCount + 63) / 64];
        var wordCount = 0;
        for (var node = 0; node < nodeCount; node++)
        {
            if ((data[offset + (node >> 3)] & (1 << (node & 7))) != 0)
            {
                _isWord[node >> 6] |= 1UL << (node & 63);
                wordCount++;
            }
        }

        offset += (nodeCount + 7) / 8;
        WordCount = wordCount;
        if ((flags & HasValuesFlag) != 0)
        {
            if (data.Length - offset < wordCount)
                throw new InvalidDataException("The word list is invalid");

            // The values are stored by word; they are kept by node so that a match reads its value directly
            _values = new byte[nodeCount];
            for (var node = 0; node < nodeCount; node++)
            {
                if (IsWord(node))
                {
                    _values[node] = data[offset];
                    offset++;
                }
            }
        }

        if (offset != data.Length)
            throw new InvalidDataException("The word list is invalid");
    }

    /// <summary>Gets the version of ICU whose word lists are embedded in this package.</summary>
    public static string IcuVersion => EmbeddedIcuVersion;

    /// <summary>Gets the word list.</summary>
    public IcuWordDictionaryKind Kind { get; }

    /// <summary>Gets the number of words.</summary>
    public int WordCount { get; }

    /// <summary>Gets a word list, loading it on first use.</summary>
    /// <param name="kind">The word list.</param>
    /// <returns>The word list. The same instance is returned for each call.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a value of <see cref="IcuWordDictionaryKind"/>.</exception>
    public static IcuWordDictionary Get(IcuWordDictionaryKind kind)
    {
        if ((uint)kind >= (uint)Dictionaries.Length)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null);

        return Dictionaries[(int)kind].Value;
    }

    /// <summary>
    /// Creates a <see cref="LineBreakDictionary"/> with the words of the lists of Thai, Lao, Khmer and Burmese, the scripts whose
    /// characters have the line breaking class Complex_Context (SA).
    /// </summary>
    /// <returns>A new dictionary. Creating it takes a few hundred milliseconds: keep it and share it.</returns>
    /// <remarks>
    /// <see cref="LineBreakDictionary"/> segments a text with its own rule (the fewest characters outside of the words of the
    /// list, then the fewest words), which is not the heuristic of the break engines of ICU (<see cref="IcuDictionaryBreakEngine"/>):
    /// the breaks are close to the ones of ICU, but not always the same.
    /// </remarks>
    public static LineBreakDictionary CreateLineBreakDictionary()
    {
        var words = new StringBuilder();
        foreach (var kind in (ReadOnlySpan<IcuWordDictionaryKind>)[IcuWordDictionaryKind.Thai, IcuWordDictionaryKind.Lao, IcuWordDictionaryKind.Khmer, IcuWordDictionaryKind.Burmese])
        {
            foreach (var word in Get(kind).EnumerateWords())
            {
                words.Append(word).Append('\n');
            }
        }

        var options = new LineBreakDictionaryOptions
        {
            MaxInputLength = long.MaxValue,
            MaxWordCount = int.MaxValue,
            MaxWordLength = 255,
        };

        return LineBreakDictionary.Parse(words.ToString(), options);
    }

    /// <summary>Determines whether a word is in the list.</summary>
    /// <param name="word">The word. The comparison is ordinal.</param>
    /// <returns><see langword="true"/> when the word is in the list.</returns>
    public bool Contains(ReadOnlySpan<char> word) => TryGetValue(word, out _);

    /// <summary>Gets whether a word is in the list, and its value.</summary>
    /// <param name="word">The word. The comparison is ordinal.</param>
    /// <param name="value">The value of the word: its cost for the list of Chinese and Japanese (the lower, the more frequent), 0 for the other lists.</param>
    /// <returns><see langword="true"/> when the word is in the list.</returns>
    public bool TryGetValue(ReadOnlySpan<char> word, out int value)
    {
        var node = 0;
        var index = 0;
        while (index < word.Length)
        {
            var codePoint = DecodeAt(word, index, out var length);
            index += length;
            if (_byteLetters && (codePoint - _codePointBase is < 0 or > 0xFD) && codePoint is not (0x200C or 0x200D))
            {
                // A character outside of the block of the script is in no word
                node = -1;
            }
            else
            {
                node = Next(node, codePoint);
            }

            if (node < 0)
            {
                value = 0;
                return false;
            }
        }

        if (node > 0 && IsWord(node))
        {
            value = _values is null ? 0 : _values[node];
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Finds the words of the list that start the text at <paramref name="start"/>, as <c>DictionaryMatcher::matches</c> of
    /// ICU: the text is read by code point, from <paramref name="start"/> until no word continues what was read, or until
    /// <paramref name="maxLength"/> UTF-16 code units were read. The lengths and the values of the words found are written
    /// in increasing length order (at most the length of the smallest destination), and their number is returned.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The index of the first code unit of the words.</param>
    /// <param name="maxLength">The maximum number of UTF-16 code units to read.</param>
    /// <param name="codeUnitLengths">Receives the length of each word, in UTF-16 code units.</param>
    /// <param name="codePointLengths">Receives the length of each word, in code points.</param>
    /// <param name="values">Receives the value of each word (0 for a list without values).</param>
    /// <param name="prefix">The number of code points that were read: the longest prefix of a word, and the code point that does not continue it.</param>
    internal int Matches(ReadOnlySpan<char> text, int start, int maxLength, Span<int> codeUnitLengths, Span<int> codePointLengths, Span<int> values, out int prefix)
    {
        var limit = Math.Min(codeUnitLengths.Length, Math.Min(codePointLengths.Length, values.Length));
        var node = 0;
        var count = 0;
        var codePoints = 0;
        var index = start;
        while (index < text.Length)
        {
            var codePoint = DecodeAt(text, index, out var length);
            index += length;
            codePoints++;
            node = Next(node, codePoint);
            if (node < 0)
                break;

            if (IsWord(node))
            {
                if (count < limit)
                {
                    codeUnitLengths[count] = index - start;
                    codePointLengths[count] = codePoints;
                    values[count] = _values is null ? 0 : _values[node];
                    count++;
                }

                // No longer word continues it (USTRINGTRIE_FINAL_VALUE)
                if (_firstChild[node] == _firstChild[node + 1])
                    break;
            }

            if (index - start >= maxLength)
                break;
        }

        prefix = codePoints;
        return count;
    }

    /// <summary>Enumerates the words of the list, in the ordinal order of their letters.</summary>
    internal IEnumerable<string> EnumerateWords()
    {
        var word = new StringBuilder();
        var stack = new Stack<(int Node, int Depth)>();
        stack.Push((0, 0));
        while (stack.TryPop(out var item))
        {
            word.Length = item.Depth;
            if (item.Node != 0)
            {
                AppendLetter(word, _letters[item.Node]);
                if (IsWord(item.Node))
                    yield return word.ToString();
            }

            for (var child = _firstChild[item.Node + 1] - 1; child >= _firstChild[item.Node]; child--)
            {
                stack.Push((child, word.Length));
            }
        }
    }

    /// <summary>Decodes the code point at an index: an unpaired surrogate is returned as is, as UText does.</summary>
    internal static int DecodeAt(ReadOnlySpan<char> text, int index, out int length)
    {
        var value = text[index];
        if (char.IsHighSurrogate(value) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(value, text[index + 1]);
        }

        length = 1;
        return value;
    }

    /// <summary>Reads a word list from its uncompressed content (see the format in the constructor).</summary>
    internal static IcuWordDictionary Load(IcuWordDictionaryKind kind, ReadOnlySpan<byte> data) => new(kind, data);

    /// <summary>Reads a word list from the content of its resource: the length of the uncompressed content (u32, little endian), then the content compressed with Brotli.</summary>
    internal static IcuWordDictionary LoadCompressed(IcuWordDictionaryKind kind, Stream stream) => new(kind, Decompress(stream));

    /// <summary>Decompresses the content of the resource of a word list.</summary>
    internal static byte[] Decompress(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        stream.ReadExactly(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < HeaderSize)
            throw new InvalidDataException("The word list is invalid");

        var data = new byte[length];
        using (var brotli = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true))
        {
            brotli.ReadExactly(data);
            if (brotli.ReadByte() >= 0)
                throw new InvalidDataException("The word list is invalid");
        }

        return data;
    }

    /// <summary>Gets the name of the embedded resource of a word list (the name of the source file of ICU).</summary>
    internal static string GetFileName(IcuWordDictionaryKind kind) => kind switch
    {
        IcuWordDictionaryKind.ChineseJapanese => "cjdict",
        IcuWordDictionaryKind.Thai => "thaidict",
        IcuWordDictionaryKind.Lao => "laodict",
        IcuWordDictionaryKind.Khmer => "khmerdict",
        IcuWordDictionaryKind.Burmese => "burmesedict",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
    };

    private static Lazy<IcuWordDictionary>[] CreateDictionaries()
    {
        var result = new Lazy<IcuWordDictionary>[(int)IcuWordDictionaryKind.Burmese + 1];
        for (var i = 0; i < result.Length; i++)
        {
            var kind = (IcuWordDictionaryKind)i;
            result[i] = new Lazy<IcuWordDictionary>(() => LoadResource(kind), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        return result;
    }

    private static IcuWordDictionary LoadResource(IcuWordDictionaryKind kind)
    {
        var name = ResourcePrefix + GetFileName(kind) + ".dict";
        using var stream = typeof(IcuWordDictionary).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"The word list '{name}' is missing");
        return LoadCompressed(kind, stream);
    }

    private void AppendLetter(StringBuilder builder, char letter)
    {
        if (!_byteLetters)
        {
            builder.Append(letter);
            return;
        }

        builder.Append(letter switch
        {
            (char)0xFE => (char)0x200C,
            (char)0xFF => (char)0x200D,
            _ => (char)(_codePointBase + letter),
        });
    }

    private bool IsWord(int node) => (_isWord[node >> 6] & (1UL << (node & 63))) != 0;

    /// <summary>Gets the node after a code point, or -1 when no word continues with it.</summary>
    private int Next(int node, int codePoint)
    {
        if (_byteLetters)
        {
            // BytesDictionaryMatcher::transform: a character outside of the block is U_SENTINEL, which BytesTrie::next
            // reads as the byte 0xFF, the letter of U+200D
            var letter = codePoint switch
            {
                0x200C => 0xFE,
                0x200D => 0xFF,
                _ => codePoint - _codePointBase is >= 0 and <= 0xFD and var delta ? delta : 0xFF,
            };

            return FindChild(node, (char)letter);
        }

        if (codePoint <= 0xFFFF)
            return FindChild(node, (char)codePoint);

        // UCharsTrie::nextForCodePoint: the two code units of a supplementary code point
        node = FindChild(node, (char)(((codePoint - 0x10000) >> 10) + 0xD800));
        return node < 0 ? -1 : FindChild(node, (char)(((codePoint - 0x10000) & 0x3FF) + 0xDC00));
    }

    private int FindChild(int node, char letter)
    {
        var low = _firstChild[node];
        var high = _firstChild[node + 1] - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var value = _letters[middle];
            if (value == letter)
                return middle;

            if (value < letter)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private static int ReadVarUInt(ReadOnlySpan<byte> data, ref int offset)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            if (offset >= data.Length)
                throw new InvalidDataException("The word list is invalid");

            var value = data[offset++];
            result |= (value & 0x7F) << shift;
            if (value < 0x80)
                return result;

            shift += 7;
            if (shift > 28)
                throw new InvalidDataException("The word list is invalid");
        }
    }
}
