using System.Buffers.Binary;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// A word list of ICU (<c>icu4c/source/data/brkitr/dictionaries/*dict.txt</c>): a trie of the words, read from a chunk of
/// <c>Intl/Data/dictionaries.bin</c> (see <see cref="WordDictionaries"/>). It is immutable: an instance is shared by all the threads.
/// </summary>
/// <remarks>
/// <para>The format of a chunk, little endian:</para>
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
internal sealed class WordDictionary
{
    public const byte FormatVersion = 1;

    /// <summary>The letters are bytes: the offset of the characters from the code point base.</summary>
    public const byte ByteLettersFlag = 1;

    /// <summary>The words have a value (the cost of the words of the Chinese and Japanese list).</summary>
    public const byte HasValuesFlag = 2;

    private const int HeaderSize = 12;

    private readonly bool _byteLetters;
    private readonly int _codePointBase;

    // The index of the first child of each node, and of the node after the last one: the children of a node are the
    // nodes from _firstChild[node] to _firstChild[node + 1], excluded
    private readonly int[] _firstChild;
    private readonly char[] _letters;
    private readonly ulong[] _isWord;
    private readonly byte[]? _values;

    private WordDictionary(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || data[0] != FormatVersion)
            throw new InvalidDataException("The word list has an unsupported format");

        var flags = data[1];
        _byteLetters = (flags & ByteLettersFlag) != 0;
        _codePointBase = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var nodeCount = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var offset = HeaderSize;

        _firstChild = new int[nodeCount + 1];
        var next = 1;
        for (var node = 0; node < nodeCount; node++)
        {
            _firstChild[node] = next;
            next += ReadVarUInt(data, ref offset);
        }

        _firstChild[nodeCount] = next;
        if (next != nodeCount)
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

    /// <summary>Gets the number of words.</summary>
    public int WordCount { get; }

    /// <summary>Reads a word list from the content of its chunk.</summary>
    public static WordDictionary Load(ReadOnlySpan<byte> data) => new(data);

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
    public int Matches(ReadOnlySpan<char> text, int start, int maxLength, Span<int> codeUnitLengths, Span<int> codePointLengths, Span<int> values, out int prefix)
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

    /// <summary>Gets whether a word is in the list, and its value.</summary>
    public bool TryGetValue(ReadOnlySpan<char> word, out int value)
    {
        Span<int> codeUnitLengths = stackalloc int[64];
        Span<int> codePointLengths = stackalloc int[64];
        Span<int> values = stackalloc int[64];
        var count = Matches(word, 0, word.Length, codeUnitLengths, codePointLengths, values, out _);
        if (count > 0 && codeUnitLengths[count - 1] == word.Length)
        {
            value = values[count - 1];
            return true;
        }

        value = 0;
        return false;
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
