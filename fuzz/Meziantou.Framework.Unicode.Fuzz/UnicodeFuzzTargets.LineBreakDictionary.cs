using System.Globalization;
using System.Text.Unicode;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    private const int DictionaryPieceLength = 1024;
    private const int DictionaryKeptLength = 512;

    /// <summary>
    /// The input is an option byte (the overload of <c>Parse</c> and the tailoring), then the word list and the text, separated by
    /// <see cref="PartSeparator"/>. <c>Parse</c> accepts exactly the lists whose words are well-formed and short enough,
    /// <c>WordCount</c> and <c>Contains</c> agree with the lines of the list, and <c>FindBreaks</c> gives the breaks of
    /// the segmentation that the remarks of <see cref="LineBreakDictionary"/> specify, computed directly (dynamic
    /// programming over each piece). For the tailoring of the input, the breaks of <c>EnumerateLineBreaks</c> with the dictionary
    /// are those without it plus the breaks of <c>FindBreaks</c> in each run of Complex_Context characters, except with
    /// BreakAll and Anywhere, where the dictionary changes nothing. <c>Load</c> and <c>LoadAsync</c>, from a stream that returns a
    /// few bytes per read, agree with <c>Parse</c>.
    /// </summary>
    private static void LineBreakDictionaryTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var listBytes = FuzzInput.ReadUntil(ref data, PartSeparator);
        var list = FuzzText.Decode(listBytes);
        var text = FuzzText.Decode(data);
        var useUtf8 = (flags & 1) != 0;

        var (expectedWords, isValid) = ParseReferenceWordList(list);
        if (useUtf8 && !Utf8.IsValid(listBytes))
        {
            isValid = false;
        }

        CheckLineBreakDictionaryLoaders(listBytes.ToArray(), text, maxReadLength: (flags & 0x20) != 0 ? 3 : int.MaxValue);

        LineBreakDictionary dictionary;
        try
        {
            dictionary = useUtf8 ? LineBreakDictionary.Parse(listBytes) : LineBreakDictionary.Parse(list);
        }
        catch (FormatException)
        {
            FuzzAssert.False(isValid, "Parse rejects a list whose words are well-formed and shorter than MaxWordLength");
            return;
        }

        FuzzAssert.True(isValid, "Parse accepts invalid UTF-8, an unpaired surrogate or a word longer than MaxWordLength");
        FuzzAssert.Equal(expectedWords.Count, dictionary.WordCount, "WordCount is not the number of distinct words of the list");
        foreach (var word in expectedWords)
        {
            FuzzAssert.True(dictionary.Contains(word), $"Contains returns false for the word {FuzzAssert.Format(word)} of the list");
        }

        var lookup = expectedWords.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var start = 0; start < Math.Min(text.Length, 32); start++)
        {
            for (var length = 0; length <= 8 && start + length <= text.Length; length++)
            {
                var candidate = text.AsSpan(start, length);
                FuzzAssert.Equal(lookup.Contains(candidate), dictionary.Contains(candidate), $"Contains({FuzzAssert.Format(candidate.ToString())}) does not tell whether the list has the word");
            }
        }

        var breaks = FindDictionaryBreaks(dictionary, text);
        var expectedBreaks = ReferenceFindBreaks(text, expectedWords);
        FuzzAssert.Equal(FormatPositions(expectedBreaks), FormatPositions(breaks), "FindBreaks does not give the breaks of the documented segmentation");

        var runBreaks = new List<int>();
        foreach (var (start, end) in GetComplexContextRuns(text))
        {
            runBreaks.AddRange(FindDictionaryBreaks(dictionary, text[start..end]).Select(index => start + index));
        }

        // One tailoring per input: enumerating the breaks with every tailoring is too slow for long texts
        var strictness = Strictnesses[((flags >> 1) & 3) % Strictnesses.Length];
        var wordBreak = WordBreaks[((flags >> 3) & 3) % WordBreaks.Length];
        var expected = new SortedDictionary<int, bool>();
        foreach (var lineBreak in GetLineBreaks(text, dictionary: null, strictness, wordBreak))
        {
            expected.Add(lineBreak.Index, lineBreak.IsMandatory);
        }

        if (strictness is not UnicodeLineBreakStrictness.Anywhere && wordBreak is not UnicodeWordBreakBehavior.BreakAll)
        {
            foreach (var index in runBreaks)
            {
                expected.TryAdd(index, value: false);
            }
        }

        var actual = GetLineBreaks(text, dictionary, strictness, wordBreak);
        FuzzAssert.Equal(FormatLineBreaks(expected.Select(item => (item.Key, item.Value))), FormatLineBreaks(actual.Select(item => (item.Index, item.IsMandatory))), $"The line breaks with the dictionary, {strictness} and {wordBreak}, are not those without it plus those of each run");
    }

    /// <summary>Load and LoadAsync read the stream to its end and give the dictionary Parse gives for the bytes, or throw the same FormatException.</summary>
    private static void CheckLineBreakDictionaryLoaders(byte[] list, string text, int maxReadLength)
    {
        var expected = DescribeLineBreakDictionary(() => LineBreakDictionary.Parse(list), text);
        FuzzAssert.Equal(expected, DescribeLineBreakDictionary(() => LoadLineBreakDictionary(list, maxReadLength), text), "Load(Stream) and Parse(bytes) differ");
        FuzzAssert.Equal(expected, DescribeLineBreakDictionary(() => LoadLineBreakDictionaryAsync(list, maxReadLength).GetAwaiter().GetResult(), text), "LoadAsync(Stream) and Parse(bytes) differ");
    }

    private static LineBreakDictionary LoadLineBreakDictionary(byte[] list, int maxReadLength)
    {
        using var stream = new ShortReadStream(list, maxReadLength);
        return LineBreakDictionary.Load(stream);
    }

    private static async Task<LineBreakDictionary> LoadLineBreakDictionaryAsync(byte[] list, int maxReadLength)
    {
        await using var stream = new ShortReadStream(list, maxReadLength);
        return await LineBreakDictionary.LoadAsync(stream).ConfigureAwait(false);
    }

    private static string DescribeLineBreakDictionary(Func<LineBreakDictionary> create, string text)
    {
        LineBreakDictionary dictionary;
        try
        {
            dictionary = create();
        }
        catch (FormatException)
        {
            return nameof(FormatException);
        }

        var positions = new int[text.Length];
        var count = dictionary.FindBreaks(text, positions);
        return $"words: {dictionary.WordCount.ToString(CultureInfo.InvariantCulture)}, breaks: {FormatPositions(positions[..count])}";
    }

    private static string FormatLineBreaks(IEnumerable<(int Index, bool IsMandatory)> breaks) => string.Join(' ', breaks.Select(item => item.Index.ToString(CultureInfo.InvariantCulture) + (item.IsMandatory ? "!" : "")));

    /// <summary>Calls FindBreaks and checks the breaks: increasing, inside the text, at grapheme cluster boundaries that are not before a combining mark, and the behavior of a buffer that is too small.</summary>
    private static int[] FindDictionaryBreaks(LineBreakDictionary dictionary, string text)
    {
        var positions = new int[text.Length];
        var count = dictionary.FindBreaks(text, positions);
        FuzzAssert.True(count >= 0 && count <= text.Length, $"FindBreaks returns {count} for a text of {text.Length} characters");
        var result = positions[..count];
        if (count == 0)
            return result;

        var graphemeBoundaries = GetGraphemeBoundaries(text);
        var previous = 0;
        foreach (var position in result)
        {
            FuzzAssert.True(position > previous && position < text.Length, $"The break {position} is not after the previous one, or is not inside the text");
            FuzzAssert.True(graphemeBoundaries[position], $"The break {position} is inside a grapheme cluster");
            FuzzAssert.False(IsLineBreakCombiningMark(CodePointAt(text, position, out _)), $"The break {position} is before a combining mark");
            previous = position;
        }

        FuzzAssert.Equal(count, dictionary.FindBreaks(text, new int[count]), "FindBreaks fails with a buffer of the number of breaks");
        try
        {
            dictionary.FindBreaks(text, new int[count - 1]);
            FuzzAssert.Fail("FindBreaks does not throw when the buffer is too small");
        }
        catch (ArgumentException ex) when (ex is not ArgumentOutOfRangeException)
        {
        }

        return result;
    }

    /// <summary>Reads a word list as the remarks of <see cref="LineBreakDictionary"/> specify it, and tells whether <c>Parse</c> must accept it with the default options.</summary>
    private static (HashSet<string> Words, bool IsValid) ParseReferenceWordList(string list)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var isValid = true;
        if (list.StartsWith('\uFEFF', StringComparison.Ordinal))
        {
            list = list[1..];
        }

        foreach (var rawLine in list.Split('\n', '\r'))
        {
            var line = rawLine;
            var end = line.AsSpan().IndexOfAny('\t', '#');
            if (end >= 0)
            {
                line = line[..end];
            }

            line = line.AsSpan().Trim().ToString();
            if (line.Length == 0)
                continue;

            if (line.Length > LineBreakDictionaryOptions.Default.MaxWordLength || !IsWellFormed(line))
            {
                isValid = false;
            }

            words.Add(line);
        }

        return (words, isValid);
    }

    /// <summary>Determines whether rule LB9 attaches a character to the character before it: the classes CM and ZWJ, and the marks of the class SA (LB1).</summary>
    private static bool IsLineBreakCombiningMark(int codePoint)
    {
        return UnicodeProperties.GetLineBreakClass(codePoint) switch
        {
            UnicodeLineBreakClass.CombiningMark or UnicodeLineBreakClass.ZWJ => true,
            UnicodeLineBreakClass.ComplexContext => UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark,
            _ => false,
        };
    }

    /// <summary>
    /// Applies the documented segmentation to a text of any length: the boundaries are the grapheme cluster boundaries
    /// that are not before a combining mark; each piece of at most 1,024 code units is segmented by dynamic programming
    /// (fewest uncovered code units, then fewest words, then the longest word first), and its words and uncovered
    /// clusters that start in its first 512 code units are kept when the text continues after it.
    /// </summary>
    private static List<int> ReferenceFindBreaks(string text, HashSet<string> words)
    {
        var result = new List<int>();
        if (words.Count == 0)
            return result;

        var lookup = words.GetAlternateLookup<ReadOnlySpan<char>>();
        var maxWordLength = words.Max(word => word.Length);
        var pieceStart = 0;
        while (pieceStart < text.Length)
        {
            var rest = text.AsSpan(pieceStart);
            var boundaries = new List<int> { 0 };
            var isLastPiece = true;
            var firstClusterLength = 0;
            while (boundaries[^1] < rest.Length)
            {
                var next = boundaries[^1] + Unicode.GetGraphemeClusterLength(rest[boundaries[^1]..]);
                while (next < rest.Length && IsLineBreakCombiningMark(CodePointAt(text, pieceStart + next, out _)))
                {
                    next += Unicode.GetGraphemeClusterLength(rest[next..]);
                }

                if (boundaries.Count == 1)
                {
                    firstClusterLength = next;
                }

                if (next > DictionaryPieceLength)
                {
                    isLastPiece = false;
                    break;
                }

                boundaries.Add(next);
            }

            if (boundaries.Count == 1)
            {
                // A cluster longer than a piece is uncovered
                pieceStart += firstClusterLength;
                continue;
            }

            var last = boundaries.Count - 1;
            var costs = new (int Uncovered, int WordCount)[boundaries.Count];
            var choices = new int[boundaries.Count];
            for (var i = last - 1; i >= 0; i--)
            {
                costs[i] = (costs[i + 1].Uncovered + boundaries[i + 1] - boundaries[i], costs[i + 1].WordCount);
                choices[i] = -1;
                for (var j = i + 1; j <= last && boundaries[j] - boundaries[i] <= maxWordLength; j++)
                {
                    // A word is preferred to an uncovered cluster of the same cost, and a longer word to a shorter one
                    var cost = (costs[j].Uncovered, WordCount: costs[j].WordCount + 1);
                    if (cost.CompareTo(costs[i]) <= 0 && lookup.Contains(rest[boundaries[i]..boundaries[j]]))
                    {
                        costs[i] = cost;
                        choices[i] = j;
                    }
                }
            }

            var boundary = 0;
            while (boundary < last && (isLastPiece || boundaries[boundary] < DictionaryKeptLength))
            {
                if (choices[boundary] < 0)
                {
                    boundary++;
                }
                else
                {
                    if (pieceStart + boundaries[boundary] > 0)
                    {
                        result.Add(pieceStart + boundaries[boundary]);
                    }

                    boundary = choices[boundary];
                }
            }

            pieceStart += boundaries[boundary];
        }

        return result;
    }

    /// <summary>Finds the runs of Complex_Context characters as the documentation of the enumerator specifies them: from their first letter.</summary>
    private static List<(int Start, int End)> GetComplexContextRuns(string text)
    {
        var runs = new List<(int Start, int End)>();
        var start = -1;
        for (var index = 0; index <= text.Length;)
        {
            var length = 1;
            var isComplexContext = false;
            var codePoint = 0;
            if (index < text.Length)
            {
                codePoint = CodePointAt(text, index, out length);
                isComplexContext = UnicodeProperties.GetLineBreakClass(codePoint) is UnicodeLineBreakClass.ComplexContext;
            }

            if (isComplexContext)
            {
                if (start < 0 && !IsLineBreakCombiningMark(codePoint))
                {
                    start = index;
                }
            }
            else
            {
                if (start >= 0)
                {
                    runs.Add((start, index));
                }

                start = -1;
            }

            index += length;
        }

        return runs;
    }

    private static IEnumerable<FuzzSeed> GetLineBreakDictionarySeeds()
    {
        var words = string.Join('\n', ThaiWords);
        (string Name, byte Flags, string List, string Text)[] seeds =
        [
            ("thai", 0, words, "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22"),
            ("thai-utf8", 1, words, "Hello \u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35\u0E04\u0E23\u0E31\u0E1A world"),
            ("punctuation", 0x02, words, "(\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22), \u0E44\u0E17\u0E22\u0E20\u0E32\u0E29\u0E32!"),
            ("uncovered", 0x10, words, "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22\u0E01\u0E02 \u0E14\u0E35\u0E46\u0E04\u0E23\u0E31\u0E1A"),
            ("marks", 0x08, words, "a\u0E31\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22 \u0E31\u0E20\u0E32\u0E29\u0E32\u0301\u0E44\u0E17\u0E22"),
            ("zwsp", 0x04, words, "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22\u200B\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35\u2060\u0E44\u0E17\u0E22\u00A0\u0E20\u0E32\u0E29\u0E32"),
            ("lao-khmer-myanmar", 0x13, words, "\u0EAA\u0EB0\u0E9A\u0EB2\u0E8D\u0E94\u0EB5 \u1797\u17B6\u179F\u17B6\u1781\u17D2\u1798\u17C2\u179A\u17D4 \u1019\u103C\u1014\u103A\u1019\u102C\u1005\u102C"),
            ("short-reads", 0x21, words, "\u0E44\u0E17\u0E22\u0E20\u0E32\u0E29\u0E32"),
            ("comments", 1, "# words\n\uFEFF\u0E20\u0E32\u0E29\u0E32\t120\n  \u0E44\u0E17\u0E22  \r\n\r\n\u0E20\u0E32\u0E29\u0E32\n", "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22\u0E20\u0E32\u0E29\u0E32"),
            ("long", 0, words, string.Concat(Enumerable.Repeat("\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35\u0E04\u0E23\u0E31\u0E1A\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22", 60))),
            ("invalid-surrogate", 0, "a\uD800\n", "a"),
            ("empty", 0, "", "\u0E20\u0E32\u0E29\u0E32"),
        ];

        foreach (var (name, flags, list, text) in seeds)
        {
            yield return new FuzzSeed(name, (byte[])[flags, .. FuzzText.Encode(list), PartSeparator, .. FuzzText.Encode(text)]);
        }
    }
}
