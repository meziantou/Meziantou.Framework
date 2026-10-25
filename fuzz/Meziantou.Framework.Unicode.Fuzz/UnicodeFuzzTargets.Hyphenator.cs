using System.Globalization;
using System.Text;
using System.Text.Unicode;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    private const int MaxHyphenatedWords = 16;

    /// <summary>
    /// The input is an option byte (the minimums, the overload, a small maximum word length), then the patterns, the
    /// exceptions and the words, separated by <see cref="PartSeparator"/> (one word per line). Malformed patterns only
    /// throw <see cref="FormatException"/>. The breaks are increasing, inside the word, at grapheme cluster boundaries,
    /// and leave the minimum number of clusters on each side; a buffer of the length of the word is enough, and a buffer
    /// one element too short throws <see cref="ArgumentException"/>. <c>Load</c> and <c>LoadAsync</c>, from streams that return a few bytes per read, agree with <c>Parse</c>. When there is no exception and no TeX group, the
    /// breaks are compared with a direct application of every pattern at every position of the word (Liang's algorithm
    /// without the trie).
    /// </summary>
    private static void Hyphenation(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var options = new HyphenatorOptions
        {
            LeftHyphenMin = 1 + (flags & 3),
            RightHyphenMin = 1 + ((flags >> 2) & 3),
            MaxWordLength = (flags & 0x20) != 0 ? 8 : HyphenatorOptions.Default.MaxWordLength,
        };
        var patternBytes = FuzzInput.ReadUntil(ref data, PartSeparator);
        var exceptionBytes = FuzzInput.ReadUntil(ref data, PartSeparator);
        var patterns = FuzzText.Decode(patternBytes);
        var exceptions = FuzzText.Decode(exceptionBytes);
        var useUtf8 = (flags & 0x10) != 0;
        var words = FuzzText.Decode(data).Split('\n').Take(MaxHyphenatedWords).ToArray();
        CheckHyphenatorLoaders(patternBytes.ToArray(), exceptionBytes.ToArray(), options, words, nullExceptions: (flags & 0x40) != 0, maxReadLength: (flags & 0x80) != 0 ? 3 : int.MaxValue);

        Hyphenator hyphenator;
        try
        {
            hyphenator = useUtf8 ? Hyphenator.Parse(patternBytes, exceptionBytes, options) : Hyphenator.Parse(patterns, exceptions, options);
        }
        catch (FormatException)
        {
            return;
        }

        FuzzAssert.False(useUtf8 && !(Utf8.IsValid(patternBytes) && Utf8.IsValid(exceptionBytes)), "The UTF-8 overload accepts invalid UTF-8");
        FuzzAssert.Equal(options.LeftHyphenMin, hyphenator.LeftHyphenMin, "LeftHyphenMin is not the one of the options");
        FuzzAssert.Equal(options.RightHyphenMin, hyphenator.RightHyphenMin, "RightHyphenMin is not the one of the options");

        // The reference only reads plain patterns: no TeX group, no comment, no exception
        var referencePatterns = exceptions.Length == 0 && hyphenator.ExceptionCount == 0 && patterns.AsSpan().IndexOfAny("\\%{}") < 0 && (!useUtf8 || !patterns.StartsWith('\uFEFF', StringComparison.Ordinal)) ? ParseReferencePatterns(patterns) : null;
        if (referencePatterns is not null)
        {
            FuzzAssert.Equal(referencePatterns.Count, hyphenator.PatternCount, "PatternCount is not the number of distinct patterns");
        }

        foreach (var word in words)
        {
            var breaks = CheckHyphenation(hyphenator, word, options);
            if (referencePatterns is not null)
            {
                var expected = ReferenceHyphenate(referencePatterns, word, options);
                FuzzAssert.Equal(FormatPositions(expected), FormatPositions(breaks), $"The breaks of {FuzzAssert.Format(word)} differ from a direct application of the patterns");
            }
        }
    }

    /// <summary>Load and LoadAsync read the streams to their end and give the hyphenator Parse gives for the bytes, or throw the same FormatException.</summary>
    private static void CheckHyphenatorLoaders(byte[] patterns, byte[] exceptions, HyphenatorOptions options, string[] words, bool nullExceptions, int maxReadLength)
    {
        var expected = DescribeHyphenator(() => Hyphenator.Parse(patterns, exceptions, options), words);
        var exceptionStreamContent = nullExceptions && exceptions.Length == 0 ? null : exceptions;
        FuzzAssert.Equal(expected, DescribeHyphenator(() => LoadHyphenator(patterns, exceptionStreamContent, options, maxReadLength), words), "Load(Stream) and Parse(bytes) differ");
        FuzzAssert.Equal(expected, DescribeHyphenator(() => LoadHyphenatorAsync(patterns, exceptionStreamContent, options, maxReadLength).GetAwaiter().GetResult(), words), "LoadAsync(Stream) and Parse(bytes) differ");
    }

    private static Hyphenator LoadHyphenator(byte[] patterns, byte[]? exceptions, HyphenatorOptions options, int maxReadLength)
    {
        using var patternStream = new ShortReadStream(patterns, maxReadLength);
        using var exceptionStream = exceptions is null ? null : new ShortReadStream(exceptions, maxReadLength);
        return Hyphenator.Load(patternStream, exceptionStream, options);
    }

    private static async Task<Hyphenator> LoadHyphenatorAsync(byte[] patterns, byte[]? exceptions, HyphenatorOptions options, int maxReadLength)
    {
        await using var patternStream = new ShortReadStream(patterns, maxReadLength);
        await using var exceptionStream = exceptions is null ? null : new ShortReadStream(exceptions, maxReadLength);
        return await Hyphenator.LoadAsync(patternStream, exceptionStream, options).ConfigureAwait(false);
    }

    private static string DescribeHyphenator(Func<Hyphenator> create, string[] words)
    {
        Hyphenator hyphenator;
        try
        {
            hyphenator = create();
        }
        catch (FormatException)
        {
            return nameof(FormatException);
        }

        var result = new StringBuilder();
        result.Append(CultureInfo.InvariantCulture, $"patterns: {hyphenator.PatternCount}, exceptions: {hyphenator.ExceptionCount}, minimums: {hyphenator.LeftHyphenMin} {hyphenator.RightHyphenMin}");
        var positions = new int[words.Length == 0 ? 0 : words.Max(word => word.Length)];
        foreach (var word in words)
        {
            var count = hyphenator.Hyphenate(word, positions);
            result.Append(' ').Append(FormatPositions(positions[..count]));
        }

        return result.ToString();
    }

    private static int[] CheckHyphenation(Hyphenator hyphenator, string word, HyphenatorOptions options)
    {
        var positions = new int[word.Length];
        var count = hyphenator.Hyphenate(word, positions);
        FuzzAssert.True(count >= 0 && count <= word.Length, $"Hyphenate returns {count} for a word of {word.Length} characters");
        if (word.Length < 2 || word.Length > options.MaxWordLength)
        {
            FuzzAssert.Equal(0, count, "A word shorter than 2 characters or longer than MaxWordLength has a break");
        }

        var result = positions[..count];
        if (count > 0)
        {
            var clusterStarts = new List<int>();
            foreach (var cluster in Unicode.EnumerateGraphemeClusters(word))
            {
                clusterStarts.Add(cluster.Start.Value);
            }

            var previous = 0;
            foreach (var position in result)
            {
                FuzzAssert.True(position > previous && position < word.Length, $"The break {position} of {FuzzAssert.Format(word)} is not after the previous one or is not inside the word");
                var clustersBefore = clusterStarts.BinarySearch(position);
                FuzzAssert.True(clustersBefore >= 0, $"The break {position} of {FuzzAssert.Format(word)} is inside a grapheme cluster");
                FuzzAssert.True(clustersBefore >= options.LeftHyphenMin && clusterStarts.Count - clustersBefore >= options.RightHyphenMin, $"The break {position} of {FuzzAssert.Format(word)} does not leave {options.LeftHyphenMin} clusters before it and {options.RightHyphenMin} after it");
                previous = position;
            }

            FuzzAssert.Equal(count, hyphenator.Hyphenate(word, new int[count]), "Hyphenate fails with a buffer of the number of breaks");
            try
            {
                hyphenator.Hyphenate(word, new int[count - 1]);
                FuzzAssert.Fail("Hyphenate does not throw when the buffer is too small");
            }
            catch (ArgumentException ex) when (ex is not ArgumentOutOfRangeException)
            {
            }
        }

        return result;
    }

    /// <summary>Reads plain patterns (tokens separated by white space) into their letters and the value before each letter; the last pattern for the same letters wins.</summary>
    private static Dictionary<string, (int[] Letters, int[] Values)> ParseReferencePatterns(string text)
    {
        var result = new Dictionary<string, (int[] Letters, int[] Values)>(StringComparer.Ordinal);
        var tokenStart = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                if (tokenStart < 0)
                {
                    tokenStart = index;
                }

                continue;
            }

            if (tokenStart >= 0)
            {
                var token = text[tokenStart..index];
                var letters = new List<int>();
                var values = new List<int> { 0 };
                for (var i = 0; i < token.Length;)
                {
                    var codePoint = CodePointAt(token, i, out var length);
                    i += length;
                    if (codePoint is >= '0' and <= '9')
                    {
                        values[^1] = codePoint - '0';
                    }
                    else
                    {
                        letters.Add(codePoint == '.' ? '.' : UnicodeProperties.ToSimpleLowercase(codePoint));
                        values.Add(0);
                    }
                }

                result[string.Join(',', letters)] = ([.. letters], [.. values]);
                tokenStart = -1;
            }
        }

        return result;
    }

    /// <summary>Slides every pattern along the word between its two boundaries, keeps the greatest value at each position, and allows the odd values that leave enough grapheme clusters on each side.</summary>
    private static List<int> ReferenceHyphenate(Dictionary<string, (int[] Letters, int[] Values)> patterns, string word, HyphenatorOptions options)
    {
        var result = new List<int>();
        if (word.Length < 2 || word.Length > options.MaxWordLength)
            return result;

        // The letters of the word between two boundaries; a dot of the word matches no letter of a pattern.
        var letters = new List<int> { '.' };
        var letterIndexes = new int[word.Length + 1];
        for (var index = 0; index < word.Length;)
        {
            var codePoint = UnicodeProperties.ToSimpleLowercase(CodePointAt(word, index, out var length));
            letterIndexes[index] = letters.Count - 1;
            letters.Add(codePoint == '.' ? -1 : codePoint);
            index += length;
        }

        letters.Add('.');

        // scores[i] is the value of the break before the letter i of the word
        var scores = new int[letters.Count - 1];
        foreach (var (patternLetters, values) in patterns.Values)
        {
            for (var start = 0; start + patternLetters.Length <= letters.Count; start++)
            {
                var matches = true;
                for (var i = 0; i < patternLetters.Length && matches; i++)
                {
                    matches = letters[start + i] == patternLetters[i];
                }

                if (!matches)
                    continue;

                for (var p = 0; p < values.Length; p++)
                {
                    var position = start + p - 1;
                    if (position >= 0 && position < scores.Length)
                    {
                        scores[position] = Math.Max(scores[position], values[p]);
                    }
                }
            }
        }

        var clusterStarts = new List<int>();
        foreach (var cluster in Unicode.EnumerateGraphemeClusters(word))
        {
            clusterStarts.Add(cluster.Start.Value);
        }

        for (var cluster = options.LeftHyphenMin; cluster <= clusterStarts.Count - options.RightHyphenMin; cluster++)
        {
            var start = clusterStarts[cluster];
            if ((scores[letterIndexes[start]] & 1) != 0)
            {
                result.Add(start);
            }
        }

        return result;
    }

    private static IEnumerable<FuzzSeed> GetHyphenatorSeeds()
    {
        (string Name, byte Flags, string Patterns, string Exceptions, string Words)[] seeds =
        [
            ("liang", 0x01, "hy3ph he2n hena4 hen5at 1na n2at 1tio 2io", "", "hyphenation\nHYPHENATION\nHyphenation\nxyz\na\n"),
            ("liang-streams", 0xC1, "hy3ph he2n hena4 hen5at 1na n2at 1tio 2io", "", "hyphenation\nconcatenation"),
            ("exceptions-streams", 0x80, "1b", "ta-ble", "table\ncable"),
            ("liang-utf8", 0x10, "hy3ph he2n hena4 hen5at 1na n2at 1tio 2io", "", "hyphenation\nconcatenation"),
            ("dots", 0x00, ".un1 1ly. 1b a2b 3b a1b a2b .a1 ..", "", "undo\nsundo\nbadly\nlyric\ncabx\nxaby\nx.ab\nab"),
            ("tex-groups", 0x05, "% comment\n\\patterns{ hy3ph he2n hena4 hen5at 1na n2at 1tio 2io }\n\\hyphenation{ ta-ble as-so-ciate }", "", "table\nassociate\nhyphenation"),
            ("exceptions", 0x00, "hy3ph he2n hena4 hen5at 1na n2at 1tio 2io 1b", "hy-phe-na-tion table A-Bcd- -xy", "Hyphenation\ntable\ncable\nabcd\nxy"),
            ("unicode", 0x00, "\u00E91t 1\u00DF 2\u0301 a1\U0001F600 e1\u0301", "", "caf\u00E9t\u00E9\nstra\u00DFe\nSTRASSE\ne\u0301te\u0301\na\U0001F600b\u00C9T\u00C9"),
            ("graphemes", 0x00, "1a 1b 1c 1\u0301 1\u200D", "", "aaaa\na\u0301b\u0301c\u0301d\nb\U0001F468\u200D\U0001F469c\u200Dd"),
            ("max-word-length", 0x20, "1a 1b", "", "abababab\nababababa"),
            ("lone-surrogates", 0x00, "a1b", "", "ab\uD800ab\nab\uDC00ab"),
        ];

        foreach (var (name, flags, patterns, exceptions, words) in seeds)
        {
            yield return new FuzzSeed(name, (byte[])[flags, .. FuzzText.Encode(patterns), PartSeparator, .. FuzzText.Encode(exceptions), PartSeparator, .. FuzzText.Encode(words)]);
        }
    }
}
