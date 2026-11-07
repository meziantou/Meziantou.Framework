using System.Collections.Frozen;

namespace Meziantou.Framework;

/// <summary>Finds the positions where a word can be hyphenated, from the hyphenation patterns of a language.</summary>
/// <remarks>
/// <para>
/// The Unicode Standard defines where a line can break (see <see cref="Unicode.EnumerateLineBreaks(ReadOnlySpan{char}, UnicodeLineBreakStrictness, UnicodeWordBreakBehavior)"/>),
/// including after a soft hyphen, but hyphenating a word needs data for its language. This class applies the pattern
/// algorithm of Franklin Liang, the one of TeX, to patterns that the caller provides: no pattern is included in this
/// package. Patterns for many languages are published by the hyph-utf8 project of the TeX community, each under its own license.
/// </para>
/// <para>The accepted input is the plain text form of the patterns:</para>
/// <list type="bullet">
/// <item><description>Patterns separated by white space, such as <c>hy3ph he2n .un1</c>: a digit between two letters gives the value of a break at that position, and a dot stands for the start or the end of a word.</description></item>
/// <item><description>Optionally inside a <c>\patterns{...}</c> group, with the exceptions in a <c>\hyphenation{...}</c> group, as in a TeX file.</description></item>
/// <item><description>Exceptions are words with their breaks, such as <c>as-so-ciate</c>; a word without a hyphen is never broken.</description></item>
/// <item><description><c>%</c> starts a comment that ends with the line.</description></item>
/// </list>
/// <para>
/// No other TeX command is interpreted: a file that uses macros, <c>\lccode</c> or the <c>^^</c> notation is rejected.
/// Hyphenation that changes the spelling of a word is not supported.
/// </para>
/// <para>An instance is immutable and can be used by several threads at the same time.</para>
/// </remarks>
public sealed class Hyphenator
{
    private const int WordBoundary = '.';

    // A letter of a word that must not match the dot of a pattern.
    private const int NoLetter = -1;

    // The greatest length of the table of the children of the root.
    private const int MaxRootTableLength = 4096;

    // The trie of the patterns: the node 0 is the root, and the edges of a node are sorted by letter.
    private readonly int[] _edgeStarts;
    private readonly int[] _edgeLetters;
    private readonly int[] _edgeTargets;
    private readonly int[] _valueOffsets;
    private readonly byte[] _values;

    // The patterns are looked up from every letter of a word, so the root also has a table indexed by letter: the
    // element of a letter is its child, or -1. It is null when the letters of the root are too far apart.
    private readonly int[]? _rootChildren;
    private readonly int _rootMinLetter;

    private readonly FrozenDictionary<string, byte[]> _exceptions;
    private readonly FrozenDictionary<string, byte[]>.AlternateLookup<ReadOnlySpan<char>> _exceptionLookup;
    private readonly int _maxWordLength;

    private Hyphenator(List<Pattern> patterns, Dictionary<string, byte[]> exceptions, HyphenatorOptions options)
    {
        LeftHyphenMin = options.LeftHyphenMin;
        RightHyphenMin = options.RightHyphenMin;
        _maxWordLength = options.MaxWordLength;
        _exceptions = exceptions.ToFrozenDictionary(StringComparer.Ordinal);
        _exceptionLookup = _exceptions.GetAlternateLookup<ReadOnlySpan<char>>();

        // Sorted patterns give the nodes in depth-first order, and the children of a node in the order of their letters.
        // The sort is stable, so that the duplicates keep the order of the input.
        var sortedPatterns = patterns.OrderBy(pattern => pattern.Letters, Comparer<int[]>.Create((left, right) => left.AsSpan().SequenceCompareTo(right)));
        var parents = new List<int> { -1 };
        var letters = new List<int> { 0 };
        var valueOffsets = new List<int> { -1 };
        var values = new List<byte>();
        var path = new List<int> { 0 };
        int[] previous = [];
        var count = 0;
        foreach (var pattern in sortedPatterns)
        {
            var common = pattern.Letters.AsSpan().CommonPrefixLength(previous);
            if (common == pattern.Letters.Length && common == previous.Length)
            {
                // The same letters twice: the last pattern wins.
                valueOffsets[path[^1]] = values.Count;
                values.AddRange(pattern.Values);
                continue;
            }

            path.RemoveRange(common + 1, path.Count - common - 1);
            for (var i = common; i < pattern.Letters.Length; i++)
            {
                parents.Add(path[^1]);
                letters.Add(pattern.Letters[i]);
                valueOffsets.Add(-1);
                path.Add(parents.Count - 1);
            }

            valueOffsets[path[^1]] = values.Count;
            values.AddRange(pattern.Values);
            previous = pattern.Letters;
            count++;
        }

        PatternCount = count;
        var nodeCount = parents.Count;
        _edgeStarts = new int[nodeCount + 1];
        for (var node = 1; node < nodeCount; node++)
        {
            _edgeStarts[parents[node] + 1]++;
        }

        for (var node = 0; node < nodeCount; node++)
        {
            _edgeStarts[node + 1] += _edgeStarts[node];
        }

        _edgeLetters = new int[nodeCount - 1];
        _edgeTargets = new int[nodeCount - 1];
        var next = new int[nodeCount];
        for (var node = 1; node < nodeCount; node++)
        {
            var parent = parents[node];
            var edge = _edgeStarts[parent] + next[parent]++;
            _edgeLetters[edge] = letters[node];
            _edgeTargets[edge] = node;
        }

        _valueOffsets = [.. valueOffsets];
        _values = [.. values];

        var rootEdgeCount = _edgeStarts[1];
        if (rootEdgeCount > 0 && _edgeLetters[rootEdgeCount - 1] - _edgeLetters[0] < MaxRootTableLength)
        {
            _rootMinLetter = _edgeLetters[0];
            _rootChildren = new int[_edgeLetters[rootEdgeCount - 1] - _rootMinLetter + 1];
            _rootChildren.AsSpan().Fill(-1);
            for (var edge = 0; edge < rootEdgeCount; edge++)
            {
                _rootChildren[_edgeLetters[edge] - _rootMinLetter] = _edgeTargets[edge];
            }
        }
    }

    /// <summary>Gets the smallest number of characters before the first break of a word.</summary>
    public int LeftHyphenMin { get; }

    /// <summary>Gets the smallest number of characters after the last break of a word.</summary>
    public int RightHyphenMin { get; }

    /// <summary>Gets the number of distinct patterns.</summary>
    public int PatternCount { get; }

    /// <summary>Gets the number of exceptions.</summary>
    public int ExceptionCount => _exceptions.Count;

    /// <summary>Loads the patterns of a language from UTF-8 files.</summary>
    /// <param name="patternsPath">The path of the file of the patterns. It can also contain the exceptions, in a <c>\hyphenation{...}</c> group.</param>
    /// <param name="exceptionsPath">The path of the file of the exceptions, or <see langword="null"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HyphenatorOptions.Default"/>.</param>
    /// <returns>The hyphenator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="patternsPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, is malformed, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="IOException">A file cannot be read.</exception>
    public static Hyphenator Load(string patternsPath, string? exceptionsPath = null, HyphenatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(patternsPath);
        options ??= HyphenatorOptions.Default;
        using var patterns = File.OpenRead(patternsPath);
        using var exceptions = exceptionsPath is null ? null : File.OpenRead(exceptionsPath);
        return Load(patterns, exceptions, options);
    }

    /// <summary>Loads the patterns of a language from UTF-8 streams.</summary>
    /// <param name="patterns">The stream of the patterns. It can also contain the exceptions, in a <c>\hyphenation{...}</c> group. It is read to its end and left open.</param>
    /// <param name="exceptions">The stream of the exceptions, or <see langword="null"/>. It is read to its end and left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HyphenatorOptions.Default"/>.</param>
    /// <returns>The hyphenator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="patterns"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, is malformed, or exceeds a limit of <paramref name="options"/>.</exception>
    public static Hyphenator Load(Stream patterns, Stream? exceptions = null, HyphenatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        options ??= HyphenatorOptions.Default;
        var patternBytes = ReadToEnd(patterns, options.MaxInputLength);
        var exceptionBytes = exceptions is null ? [] : ReadToEnd(exceptions, options.MaxInputLength);
        return Parse(patternBytes, exceptionBytes, options);
    }

    /// <summary>Loads the patterns of a language from UTF-8 files.</summary>
    /// <param name="patternsPath">The path of the file of the patterns. It can also contain the exceptions, in a <c>\hyphenation{...}</c> group.</param>
    /// <param name="exceptionsPath">The path of the file of the exceptions, or <see langword="null"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HyphenatorOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The hyphenator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="patternsPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, is malformed, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="IOException">A file cannot be read.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static async Task<Hyphenator> LoadAsync(string patternsPath, string? exceptionsPath = null, HyphenatorOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patternsPath);
        options ??= HyphenatorOptions.Default;
        var patterns = new FileStream(patternsPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (patterns.ConfigureAwait(false))
        {
            var patternBytes = await ReadToEndAsync(patterns, options.MaxInputLength, cancellationToken).ConfigureAwait(false);
            byte[] exceptionBytes = [];
            if (exceptionsPath is not null)
            {
                var exceptions = new FileStream(exceptionsPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using (exceptions.ConfigureAwait(false))
                {
                    exceptionBytes = await ReadToEndAsync(exceptions, options.MaxInputLength, cancellationToken).ConfigureAwait(false);
                }
            }

            return Parse(patternBytes, exceptionBytes, options);
        }
    }

    /// <summary>Loads the patterns of a language from UTF-8 streams.</summary>
    /// <param name="patterns">The stream of the patterns. It can also contain the exceptions, in a <c>\hyphenation{...}</c> group. It is read to its end and left open.</param>
    /// <param name="exceptions">The stream of the exceptions, or <see langword="null"/>. It is read to its end and left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HyphenatorOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The hyphenator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="patterns"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is not valid UTF-8, is malformed, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static async Task<Hyphenator> LoadAsync(Stream patterns, Stream? exceptions = null, HyphenatorOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        options ??= HyphenatorOptions.Default;
        var patternBytes = await ReadToEndAsync(patterns, options.MaxInputLength, cancellationToken).ConfigureAwait(false);
        var exceptionBytes = exceptions is null ? [] : await ReadToEndAsync(exceptions, options.MaxInputLength, cancellationToken).ConfigureAwait(false);
        return Parse(patternBytes, exceptionBytes, options);
    }

    /// <summary>Creates a hyphenator from the text of the patterns of a language.</summary>
    /// <param name="patterns">The patterns. They can also contain the exceptions, in a <c>\hyphenation{...}</c> group.</param>
    /// <param name="exceptions">The exceptions, or an empty span.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HyphenatorOptions.Default"/>.</param>
    /// <returns>The hyphenator.</returns>
    /// <exception cref="FormatException">The content is malformed, or exceeds a limit of <paramref name="options"/>.</exception>
    public static Hyphenator Parse(ReadOnlySpan<char> patterns, ReadOnlySpan<char> exceptions = default, HyphenatorOptions? options = null)
    {
        options ??= HyphenatorOptions.Default;
        if (patterns.Length > options.MaxInputLength || exceptions.Length > options.MaxInputLength)
            throw new FormatException("The hyphenation patterns exceed the maximum input length");

        var patternList = new List<Pattern>();
        var exceptionMap = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        ParseInput(patterns, areExceptions: false, patternList, exceptionMap, options);
        ParseInput(exceptions, areExceptions: true, patternList, exceptionMap, options);
        return new Hyphenator(patternList, exceptionMap, options);
    }

    /// <summary>Creates a hyphenator from the UTF-8 text of the patterns of a language.</summary>
    /// <param name="utf8Patterns">The patterns, in UTF-8. They can also contain the exceptions, in a <c>\hyphenation{...}</c> group.</param>
    /// <param name="utf8Exceptions">The exceptions, in UTF-8, or an empty span.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HyphenatorOptions.Default"/>.</param>
    /// <returns>The hyphenator.</returns>
    /// <exception cref="FormatException">The content is not valid UTF-8, is malformed, or exceeds a limit of <paramref name="options"/>.</exception>
    public static Hyphenator Parse(ReadOnlySpan<byte> utf8Patterns, ReadOnlySpan<byte> utf8Exceptions = default, HyphenatorOptions? options = null)
    {
        options ??= HyphenatorOptions.Default;
        if (utf8Patterns.Length > options.MaxInputLength || utf8Exceptions.Length > options.MaxInputLength)
            throw new FormatException("The hyphenation patterns exceed the maximum input length");

        return Parse(Decode(utf8Patterns), Decode(utf8Exceptions), options);
    }

    /// <summary>Finds the positions where a word can be hyphenated.</summary>
    /// <param name="word">The word, without the punctuation or the spaces around it. Its case does not matter.</param>
    /// <param name="breakPositions">The buffer that receives the positions, in increasing order. A position is the index of the UTF-16 code unit of <paramref name="word"/> before which a hyphen can be inserted. As many elements as the length of the word are always enough.</param>
    /// <returns>The number of positions written; 0 when the word cannot be hyphenated, or is longer than <see cref="HyphenatorOptions.MaxWordLength"/>.</returns>
    /// <remarks>
    /// A break is never inside a grapheme cluster, and it leaves at least <see cref="LeftHyphenMin"/> characters before it and
    /// <see cref="RightHyphenMin"/> after it. The method does not allocate.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="breakPositions"/> is too small.</exception>
    public int Hyphenate(ReadOnlySpan<char> word, Span<int> breakPositions)
    {
        if (word.Length < 2 || word.Length > _maxWordLength)
            return 0;

        // The lowercase letters of the word between two word boundaries, and the UTF-16 index of each letter. The
        // lowercase word is only needed to look up the exceptions.
        var hasExceptions = _exceptions.Count != 0;
        Span<int> letters = stackalloc int[word.Length + 2];
        Span<int> offsets = stackalloc int[word.Length + 1];
        Span<char> lowercase = hasExceptions ? stackalloc char[word.Length * 2] : default;
        var letterCount = 0;
        var lowercaseLength = 0;
        letters[0] = WordBoundary;
        for (var index = 0; index < word.Length;)
        {
            var codePoint = UnicodeProperties.ToSimpleLowercase(Unicode.DecodeAt(word, index, out var length));
            offsets[letterCount] = index;
            letters[++letterCount] = codePoint == WordBoundary ? NoLetter : codePoint;
            if (hasExceptions)
            {
                lowercaseLength += Encode(codePoint, lowercase[lowercaseLength..]);
            }

            index += length;
        }

        offsets[letterCount] = word.Length;
        letters[letterCount + 1] = WordBoundary;

        // scores[i] is the value of a break before the letter i of the word; odd values allow the break.
        Span<byte> scores = stackalloc byte[letterCount + 1];
        scores.Clear();
        if (hasExceptions && _exceptionLookup.TryGetValue(lowercase[..lowercaseLength], out var exception))
        {
            foreach (var position in exception)
            {
                scores[position] = 1;
            }
        }
        else
        {
            ApplyPatterns(letters[..(letterCount + 2)], scores);
        }

        // A break stays outside the grapheme clusters, and the minimums count clusters. The letters are not needed
        // anymore: their buffer receives the start of each cluster.
        var clusterStarts = letters;
        var clusterCount = 0;
        for (var index = 0; index < word.Length; index += Unicode.GetGraphemeClusterLength(word[index..]))
        {
            clusterStarts[clusterCount++] = index;
        }

        var count = 0;
        var letter = 0;
        for (var cluster = LeftHyphenMin; cluster <= clusterCount - RightHyphenMin; cluster++)
        {
            var start = clusterStarts[cluster];
            while (offsets[letter] < start)
            {
                letter++;
            }

            if ((scores[letter] & 1) != 0)
            {
                if (count == breakPositions.Length)
                    throw new ArgumentException("The destination is too small", nameof(breakPositions));

                breakPositions[count++] = start;
            }
        }

        return count;
    }

    private void ApplyPatterns(ReadOnlySpan<int> letters, Span<byte> scores)
    {
        // letters[0] and letters[^1] are the word boundaries: the position p of a pattern that starts at the letter i
        // is the break before letters[i + p], which is the letter i + p - 1 of the word.
        for (var start = 0; start < letters.Length; start++)
        {
            var node = 0;
            for (var index = start; index < letters.Length; index++)
            {
                node = FindChild(node, letters[index]);
                if (node < 0)
                    break;

                var offset = _valueOffsets[node];
                if (offset < 0)
                    continue;

                var values = _values.AsSpan(offset, index - start + 2);
                for (var p = 0; p < values.Length; p++)
                {
                    var position = start + p - 1;
                    if ((uint)position < (uint)scores.Length && values[p] > scores[position])
                    {
                        scores[position] = values[p];
                    }
                }
            }
        }
    }

    private int FindChild(int node, int letter)
    {
        if (node == 0 && _rootChildren is not null)
        {
            var offset = letter - _rootMinLetter;
            return (uint)offset < (uint)_rootChildren.Length ? _rootChildren[offset] : -1;
        }

        var low = _edgeStarts[node];
        var high = _edgeStarts[node + 1] - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            var value = _edgeLetters[middle];
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
                return _edgeTargets[middle];
            }
        }

        return -1;
    }

    private static int Encode(int codePoint, Span<char> destination)
    {
        if (codePoint > 0xFFFF)
        {
            var value = codePoint - 0x10000;
            destination[0] = (char)(0xD800 + (value >> 10));
            destination[1] = (char)(0xDC00 + (value & 0x3FF));
            return 2;
        }

        destination[0] = (char)codePoint;
        return 1;
    }

    private static string Decode(ReadOnlySpan<byte> utf8)
    {
        // A byte order mark is not part of the text.
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            utf8 = utf8[3..];
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(utf8);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("The hyphenation patterns are not valid UTF-8", exception);
        }
    }

    private static byte[] ReadToEnd(Stream stream, long maxLength)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > maxLength)
                throw new FormatException("The hyphenation patterns exceed the maximum input length");

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
                throw new FormatException("The hyphenation patterns exceed the maximum input length");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Reads the tokens of an input: patterns or exceptions, by group or by default.</summary>
    private static void ParseInput(ReadOnlySpan<char> input, bool areExceptions, List<Pattern> patterns, Dictionary<string, byte[]> exceptions, HyphenatorOptions options)
    {
        var isInGroup = false;
        var defaultKind = areExceptions;
        var kind = defaultKind;
        for (var index = 0; index < input.Length;)
        {
            var c = input[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
            }
            else if (c == '%')
            {
                var end = input[index..].IndexOfAny('\n', '\r');
                index = end < 0 ? input.Length : index + end;
            }
            else if (c == '\\')
            {
                // The only commands are the two groups; nothing else of TeX is interpreted.
                var name = input[(index + 1)..];
                bool isExceptionGroup;
                if (name.StartsWith("patterns", StringComparison.Ordinal))
                {
                    isExceptionGroup = false;
                    index += 1 + "patterns".Length;
                }
                else if (name.StartsWith("hyphenation", StringComparison.Ordinal))
                {
                    isExceptionGroup = true;
                    index += 1 + "hyphenation".Length;
                }
                else
                {
                    throw new FormatException("The hyphenation patterns contain a command other than \\patterns and \\hyphenation");
                }

                while (index < input.Length && char.IsWhiteSpace(input[index]))
                {
                    index++;
                }

                if (isInGroup || index >= input.Length || input[index] != '{')
                    throw new FormatException("A group of hyphenation patterns must start with '{' and cannot be nested");

                index++;
                isInGroup = true;
                kind = isExceptionGroup;
            }
            else if (c == '}')
            {
                if (!isInGroup)
                    throw new FormatException("The hyphenation patterns contain a '}' without a group");

                index++;
                isInGroup = false;
                kind = defaultKind;
            }
            else
            {
                var end = index;
                while (end < input.Length && !char.IsWhiteSpace(input[end]) && input[end] is not ('%' or '\\' or '{' or '}'))
                {
                    end++;
                }

                if (end < input.Length && input[end] == '{')
                    throw new FormatException("The hyphenation patterns contain a '{' without a command");

                var token = input[index..end];
                if (kind)
                {
                    AddException(token, exceptions, options);
                }
                else
                {
                    AddPattern(token, patterns, options);
                }

                index = end;
            }
        }

        if (isInGroup)
            throw new FormatException("A group of hyphenation patterns is not closed");
    }

    private static void AddPattern(ReadOnlySpan<char> token, List<Pattern> patterns, HyphenatorOptions options)
    {
        if (patterns.Count >= options.MaxPatternCount)
            throw new FormatException("There are more hyphenation patterns than the maximum pattern count");

        // The value before each letter, and one after the last letter.
        Span<int> letters = stackalloc int[Math.Min(token.Length, options.MaxPatternLength)];
        Span<byte> values = stackalloc byte[letters.Length + 1];
        values.Clear();
        var count = 0;
        var hasValue = false;
        for (var index = 0; index < token.Length;)
        {
            var c = token[index];
            if (char.IsAsciiDigit(c))
            {
                if (hasValue)
                    throw new FormatException($"The hyphenation pattern '{token}' has two digits in a row");

                values[count] = (byte)(c - '0');
                hasValue = true;
                index++;
                continue;
            }

            if (token[index] == '^' && index + 1 < token.Length && token[index + 1] == '^')
                throw new FormatException("The ^^ notation of TeX is not supported in hyphenation patterns");

            var codePoint = Unicode.DecodeAt(token, index, out var length);
            if (char.IsSurrogate((char)codePoint) && length == 1)
                throw new FormatException("The hyphenation patterns contain an unpaired surrogate");

            if (count == letters.Length)
                throw new FormatException($"The hyphenation pattern '{token}' is longer than the maximum pattern length");

            // A dot only stands for a word boundary at the two ends of a pattern.
            if (codePoint == WordBoundary && count != 0 && index + length != token.Length)
                throw new FormatException($"The hyphenation pattern '{token}' has a dot inside it");

            letters[count++] = codePoint == WordBoundary ? WordBoundary : UnicodeProperties.ToSimpleLowercase(codePoint);
            hasValue = false;
            index += length;
        }

        if (count == 0 || (count == 1 && letters[0] == WordBoundary))
            throw new FormatException($"The hyphenation pattern '{token}' has no letter");

        patterns.Add(new Pattern(letters[..count].ToArray(), values[..(count + 1)].ToArray()));
    }

    private static void AddException(ReadOnlySpan<char> token, Dictionary<string, byte[]> exceptions, HyphenatorOptions options)
    {
        if (exceptions.Count >= options.MaxPatternCount)
            throw new FormatException("There are more hyphenation exceptions than the maximum pattern count");

        // The positions are counted in letters: the break is before the letter at that position.
        Span<char> word = stackalloc char[options.MaxPatternLength * 2];
        Span<byte> positions = stackalloc byte[options.MaxPatternLength];
        var wordLength = 0;
        var letterCount = 0;
        var positionCount = 0;
        for (var index = 0; index < token.Length;)
        {
            if (token[index] == '-')
            {
                if (letterCount > 0 && (positionCount == 0 || positions[positionCount - 1] != letterCount))
                {
                    positions[positionCount++] = (byte)letterCount;
                }

                index++;
                continue;
            }

            var codePoint = Unicode.DecodeAt(token, index, out var length);
            if (char.IsSurrogate((char)codePoint) && length == 1)
                throw new FormatException("The hyphenation exceptions contain an unpaired surrogate");

            if (letterCount == options.MaxPatternLength)
                throw new FormatException($"The hyphenation exception '{token}' is longer than the maximum pattern length");

            wordLength += Encode(UnicodeProperties.ToSimpleLowercase(codePoint), word[wordLength..]);
            letterCount++;
            index += length;
        }

        if (letterCount == 0)
            throw new FormatException($"The hyphenation exception '{token}' has no letter");

        // A hyphen at the end of the word is not a break.
        if (positionCount > 0 && positions[positionCount - 1] == letterCount)
        {
            positionCount--;
        }

        exceptions[new string(word[..wordLength])] = positions[..positionCount].ToArray();
    }

    private readonly record struct Pattern(int[] Letters, byte[] Values);
}
