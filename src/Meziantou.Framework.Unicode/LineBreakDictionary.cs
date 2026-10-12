using System.Numerics;
using LineBreak = Meziantou.Framework.UnicodeLineBreakClass;

namespace Meziantou.Framework;

/// <summary>Finds the positions where a line can break in text that is written without spaces between words, from a list of the words of a language.</summary>
/// <remarks>
/// <para>
/// Thai, Lao, Khmer, Myanmar and the other scripts whose characters have the line breaking class Complex_Context (SA)
/// do not separate words with spaces. The Unicode Line Breaking Algorithm (Unicode Standard Annex #14) leaves such
/// text to a dictionary-based or another language-specific analysis, and without one it only breaks at spaces. This
/// class segments a text into the words of a list that the caller provides: no word list is included in this package.
/// The International Components for Unicode (ICU) publish word lists for Thai, Lao, Khmer and Burmese under the
/// Unicode License, with additional notices for some of them in the license file of ICU.
/// </para>
/// <para>The accepted input is plain text:</para>
/// <list type="bullet">
/// <item><description>One word per line. The white space around a word is ignored, as are the blank lines.</description></item>
/// <item><description>What follows the first tab of a line is ignored: a list can have other columns, such as a frequency.</description></item>
/// <item><description><c>#</c> starts a comment that ends with the line.</description></item>
/// <item><description>A word is kept as written: it is not normalized and its case is not changed. Pass the text in the normalization form of the word list.</description></item>
/// </list>
/// <para>
/// A text is segmented as a whole, as specified here. <see cref="Unicode.EnumerateLineBreaks(ReadOnlySpan{char}, LineBreakDictionary, UnicodeLineBreakStrictness, UnicodeWordBreakBehavior)"/>
/// applies the same rule to each run of Complex_Context characters of a text.
/// </para>
/// <list type="bullet">
/// <item><description>A boundary is the start of the text, the end of the text, or a grapheme cluster boundary that is not before a combining mark, that is a character that rule LB9 of the annex attaches to the character before it. A line never breaks inside a grapheme cluster, nor before a mark.</description></item>
/// <item><description>A segmentation is a set of occurrences of words of the dictionary in the text that do not overlap, each of them between two boundaries. The characters that are in none of them are uncovered.</description></item>
/// <item><description>The chosen segmentation has the fewest uncovered UTF-16 code units, then the fewest words. When several remain, at the first position where two of them differ the one that has the longer word at that position is preferred, a word being preferred to an uncovered character.</description></item>
/// <item><description>A line can break before each word of the chosen segmentation, except at the start of the text. Uncovered characters give no break: they stay with what precedes them, and a text that has no word has no break.</description></item>
/// <item><description>A text longer than 1,024 UTF-16 code units is segmented piece by piece, in a time that is proportional to its length: the rule is applied to the longest part of its first 1,024 code units that ends at a boundary; the words and the uncovered characters that start in the first 512 code units are kept, and the rule is applied in the same way to the text that follows them. When there is no such part, because a grapheme cluster and its marks are longer than 1,024 code units, they are uncovered.</description></item>
/// </list>
/// <para>An instance is immutable and can be used by several threads at the same time.</para>
/// </remarks>
/// <seealso href="https://www.unicode.org/reports/tr14/" />
public sealed class LineBreakDictionary
{
    /// <summary>The greatest length, in UTF-16 code units, of the text that is segmented at once.</summary>
    internal const int PieceLength = 1024;

    // The words and the uncovered characters of a piece that start before this position are final when the text continues after the piece.
    private const int KeptLength = PieceLength / 2;

    private const ushort NotBoundary = ushort.MaxValue;

    // The trie of the words, by UTF-16 code unit. The node 0 is the root, and the nodes are numbered level by level:
    // the children of a node are the consecutive nodes from _firstChildren[node] to _firstChildren[node + 1],
    // excluded, sorted by letter.
    private readonly int[] _firstChildren;
    private readonly char[] _letters;
    private readonly ulong[] _isWord;

    private LineBreakDictionary(List<string> words)
    {
        words.Sort(StringComparer.Ordinal);

        var firstChildren = new List<int>();
        var letters = new List<char> { '\0' };
        var wordNodes = new List<int>();

        // A node is the words that share a prefix: a range of the sorted list, and the length of the prefix.
        var nodes = new Queue<(int Start, int End, int Depth)>();
        nodes.Enqueue((0, words.Count, 0));
        while (nodes.TryDequeue(out var node))
        {
            // The nodes are dequeued in the order of their numbers.
            var index = firstChildren.Count;
            firstChildren.Add(letters.Count);

            // The word that is the prefix itself comes first, as many times as it is in the list.
            var start = node.Start;
            if (start < node.End && words[start].Length == node.Depth)
            {
                wordNodes.Add(index);
                while (start < node.End && words[start].Length == node.Depth)
                {
                    start++;
                }
            }

            while (start < node.End)
            {
                var letter = words[start][node.Depth];
                var end = start + 1;
                while (end < node.End && words[end][node.Depth] == letter)
                {
                    end++;
                }

                letters.Add(letter);
                nodes.Enqueue((start, end, node.Depth + 1));
                start = end;
            }
        }

        firstChildren.Add(letters.Count);
        _firstChildren = [.. firstChildren];
        _letters = [.. letters];
        _isWord = new ulong[(_letters.Length + 63) / 64];
        foreach (var node in wordNodes)
        {
            _isWord[node >> 6] |= 1UL << (node & 63);
        }

        WordCount = wordNodes.Count;
    }

    /// <summary>Gets the number of distinct words.</summary>
    public int WordCount { get; }

    /// <summary>Loads a word list from a UTF-8 file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="LineBreakDictionaryOptions.Default"/>.</param>
    /// <returns>The dictionary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static LineBreakDictionary Load(string path, LineBreakDictionaryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream, options);
    }

    /// <summary>Loads a word list from a UTF-8 stream.</summary>
    /// <param name="stream">The stream. It is read to its end and left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="LineBreakDictionaryOptions.Default"/>.</param>
    /// <returns>The dictionary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, or exceeds a limit of <paramref name="options"/>.</exception>
    public static LineBreakDictionary Load(Stream stream, LineBreakDictionaryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= LineBreakDictionaryOptions.Default;
        return Parse(ReadToEnd(stream, options.MaxInputLength), options);
    }

    /// <summary>Loads a word list from a UTF-8 file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="LineBreakDictionaryOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The dictionary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static async Task<LineBreakDictionary> LoadAsync(string path, LineBreakDictionaryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            return await LoadAsync(stream, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Loads a word list from a UTF-8 stream.</summary>
    /// <param name="stream">The stream. It is read to its end and left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="LineBreakDictionaryOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The dictionary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static async Task<LineBreakDictionary> LoadAsync(Stream stream, LineBreakDictionaryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= LineBreakDictionaryOptions.Default;
        var bytes = await ReadToEndAsync(stream, options.MaxInputLength, cancellationToken).ConfigureAwait(false);
        return Parse(bytes, options);
    }

    /// <summary>Creates a dictionary from the text of a word list.</summary>
    /// <param name="words">The word list: one word per line.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="LineBreakDictionaryOptions.Default"/>.</param>
    /// <returns>The dictionary.</returns>
    /// <exception cref="FormatException">The content has an unpaired surrogate, or exceeds a limit of <paramref name="options"/>.</exception>
    public static LineBreakDictionary Parse(ReadOnlySpan<char> words, LineBreakDictionaryOptions? options = null)
    {
        options ??= LineBreakDictionaryOptions.Default;
        if (words.Length > options.MaxInputLength)
            throw new FormatException("The word list exceeds the maximum input length");

        // A byte order mark is not part of the text.
        if (!words.IsEmpty && words[0] == (char)0xFEFF)
        {
            words = words[1..];
        }

        var list = new List<string>();
        while (!words.IsEmpty)
        {
            var lineLength = words.IndexOfAny('\n', '\r');
            var line = lineLength < 0 ? words : words[..lineLength];
            words = lineLength < 0 ? default : words[(lineLength + 1)..];

            var end = line.IndexOfAny('\t', '#');
            if (end >= 0)
            {
                line = line[..end];
            }

            line = line.Trim();
            if (line.IsEmpty)
                continue;

            if (line.Length > options.MaxWordLength)
                throw new FormatException("A word of the word list is longer than the maximum word length");

            if (list.Count >= options.MaxWordCount)
                throw new FormatException("There are more words in the word list than the maximum word count");

            for (var index = 0; index < line.Length; index++)
            {
                if (!char.IsSurrogate(line[index]))
                    continue;

                if (!char.IsHighSurrogate(line[index]) || index + 1 == line.Length || !char.IsLowSurrogate(line[index + 1]))
                    throw new FormatException("The word list contains an unpaired surrogate");

                index++;
            }

            list.Add(line.ToString());
        }

        return new LineBreakDictionary(list);
    }

    /// <summary>Creates a dictionary from the UTF-8 text of a word list.</summary>
    /// <param name="utf8Words">The word list, in UTF-8: one word per line.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="LineBreakDictionaryOptions.Default"/>.</param>
    /// <returns>The dictionary.</returns>
    /// <exception cref="FormatException">The content is not valid UTF-8, or exceeds a limit of <paramref name="options"/>.</exception>
    public static LineBreakDictionary Parse(ReadOnlySpan<byte> utf8Words, LineBreakDictionaryOptions? options = null)
    {
        options ??= LineBreakDictionaryOptions.Default;
        if (utf8Words.Length > options.MaxInputLength)
            throw new FormatException("The word list exceeds the maximum input length");

        string words;
        try
        {
            words = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(utf8Words);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("The word list is not valid UTF-8", exception);
        }

        return Parse(words, options);
    }

    /// <summary>Determines whether a word is in the dictionary.</summary>
    /// <param name="word">The word. The comparison is ordinal.</param>
    /// <returns><see langword="true"/> when the word is in the dictionary.</returns>
    public bool Contains(ReadOnlySpan<char> word)
    {
        var node = 0;
        foreach (var letter in word)
        {
            node = FindChild(node, letter);
            if (node < 0)
                return false;
        }

        return IsWord(node);
    }

    /// <summary>Finds the positions where a line can break in a text that is written without spaces between words.</summary>
    /// <param name="text">The text: a run of characters of the script of the dictionary, without the spaces or the punctuation around it. It is segmented as a whole, whatever the line breaking classes of its characters.</param>
    /// <param name="breakPositions">The buffer that receives the positions, in increasing order. A position is the index of the UTF-16 code unit of <paramref name="text"/> before which a line can break: the first one of a word of the dictionary. As many elements as the length of the text are always enough.</param>
    /// <returns>The number of positions written; 0 when the text has no word of the dictionary after its start.</returns>
    /// <remarks>
    /// The remarks of <see cref="LineBreakDictionary"/> specify the segmentation. A break is never at the start or at the end
    /// of the text, inside a grapheme cluster, or before a combining mark. The method does not allocate, whatever the length of the text.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="breakPositions"/> is too small.</exception>
    public int FindBreaks(ReadOnlySpan<char> text, Span<int> breakPositions)
    {
        if (WordCount == 0)
            return 0;

        Span<ulong> wordStarts = stackalloc ulong[PieceLength / 64];
        var count = 0;
        for (var start = 0; start < text.Length;)
        {
            var length = SegmentPiece(text[start..], wordStarts);
            for (var i = 0; i < wordStarts.Length; i++)
            {
                var bits = wordStarts[i];
                while (bits != 0)
                {
                    var position = start + (i * 64) + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;

                    // The word at the start of the text gives no break.
                    if (position == 0)
                        continue;

                    if (count == breakPositions.Length)
                        throw new ArgumentException("The destination is too small", nameof(breakPositions));

                    breakPositions[count++] = position;
                }
            }

            start += length;
        }

        return count;
    }

    /// <summary>Segments the start of a text, and finds the words of the part of it whose segmentation is final.</summary>
    /// <param name="text">The text, from the end of the previous piece to the end of the run.</param>
    /// <param name="wordStarts">A set of <see cref="PieceLength"/> bits. On return, the bit of an index is set when a word of the segmentation starts at that index of <paramref name="text"/>.</param>
    /// <returns>The length of the part of <paramref name="text"/> that is segmented: the next piece starts after it. It is not 0 unless <paramref name="text"/> is empty.</returns>
    internal int SegmentPiece(ReadOnlySpan<char> text, Span<ulong> wordStarts)
    {
        wordStarts.Clear();

        // The choice at a boundary is the length of the word that starts there, or 0 when the cluster that starts
        // there is uncovered. The cost of a boundary is the cost of the best segmentation of the text from that
        // boundary to the end of the piece: its number of uncovered code units, then its number of words.
        var limit = Math.Min(text.Length, PieceLength);
        Span<ushort> choices = stackalloc ushort[limit + 1];
        Span<int> costs = stackalloc int[limit + 1];
        choices.Fill(NotBoundary);

        var end = 0;
        while (end < limit)
        {
            var length = GetClusterLength(text[end..]);
            if (end + length > limit)
            {
                // A cluster that does not fit in a piece cannot be in a word.
                if (end == 0)
                    return length;

                break;
            }

            choices[end] = 0;
            end += length;
        }

        choices[end] = 0;
        costs[end] = 0;
        var next = end;
        for (var start = end - 1; start >= 0; start--)
        {
            if (choices[start] == NotBoundary)
                continue;

            var best = costs[next] + ((next - start) << 16);
            var choice = 0;
            var node = 0;
            for (var index = start; index < end; index++)
            {
                node = FindChild(node, text[index]);
                if (node < 0)
                    break;

                if (!IsWord(node) || choices[index + 1] == NotBoundary)
                    continue;

                // A word is preferred to an uncovered cluster that costs the same, and a longer word to a shorter one.
                var cost = costs[index + 1] + 1;
                if (cost <= best)
                {
                    best = cost;
                    choice = index + 1 - start;
                }
            }

            costs[start] = best;
            choices[start] = (ushort)choice;
            next = start;
        }

        // The end of a piece that is not the end of the text is arbitrary: what is close to it is segmented again
        // with the text that follows.
        var isLastPiece = end == text.Length;
        var position = 0;
        while (position < end && (isLastPiece || position < KeptLength))
        {
            int wordLength = choices[position];
            if (wordLength != 0)
            {
                wordStarts[position >> 6] |= 1UL << (position & 63);
                position += wordLength;
            }
            else
            {
                do
                {
                    position++;
                }
                while (choices[position] == NotBoundary);
            }
        }

        return position;
    }

    /// <summary>Gets the length of the first grapheme cluster of a text, with the clusters that follow it and start with a combining mark.</summary>
    private static int GetClusterLength(ReadOnlySpan<char> text)
    {
        var length = Unicode.GetGraphemeClusterLength(text);
        while (length < text.Length && IsCombiningMark(Unicode.DecodeAt(text, length, out _)))
        {
            length += Unicode.GetGraphemeClusterLength(text[length..]);
        }

        return length;
    }

    /// <summary>Determines whether rule LB9 attaches a character to the character before it.</summary>
    private static bool IsCombiningMark(int codePoint)
    {
        // See UnicodeTables.GetLineBreak for the layout of the tuple: the bit 15 is General_Category Mn or Mc (LB1).
        var tuple = UnicodeProperties.GetLineBreakTuple(codePoint);
        return (LineBreak)(tuple & 0xFF) switch
        {
            LineBreak.CombiningMark or LineBreak.ZWJ => true,
            LineBreak.ComplexContext => (tuple & 0x8000) != 0,
            _ => false,
        };
    }

    private bool IsWord(int node) => (_isWord[node >> 6] & (1UL << (node & 63))) != 0;

    private int FindChild(int node, char letter)
    {
        var low = _firstChildren[node];
        var high = _firstChildren[node + 1] - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            var value = _letters[middle];
            if (letter < value)
            {
                high = middle - 1;
            }
            else if (letter > value)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }

        return -1;
    }

    private static byte[] ReadToEnd(Stream stream, long maxLength)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > maxLength)
                throw new FormatException("The word list exceeds the maximum input length");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadToEndAsync(Stream stream, long maxLength, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxLength)
                throw new FormatException("The word list exceeds the maximum input length");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
