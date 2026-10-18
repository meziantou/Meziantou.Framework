using System.Globalization;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

/// <summary>
/// The algorithms of the library never throw for the content of a text: unpaired surrogates are characters of their own.
/// Each target checks the documented properties of the results (segments that cover the text, idempotent normal forms,
/// levels in range, breaks at boundaries) and compares the library with a direct implementation of the documented rule
/// when one is short enough (canonical normalization, Liang's patterns, the segmentation of a dictionary).
/// </summary>
public static partial class UnicodeFuzzTargets
{
    // Separates the parts of an input (a word list and a text, two strings, ...). It is not valid UTF-8, so a part never contains it.
    private const byte PartSeparator = 0xFF;

    private static readonly string[] TextTokens =
    [
        "\r\n", "\r", "\n", "\t", " ", "\u0085", "\u2028", "\u2029", "\u000B", "\u000C", "\u00A0", "\u00AD", "\u200B", "\u200C", "\u200D", "\u2060", "\uFEFF", "\uFFFC",
        "\u0301", "\u0308", "\u0323", "\u0345", "\u034F", "\u093F", "\u094D", "\u0915", "\u0937", "\u0E01", "\u0E31", "\u0E33", "\u0E40", "\u1019", "\u1039", "\u102C",
        "\u1100", "\u1161", "\u11A8", "\uAC00", "\uAC01", "\u3041", "\u30FC", "\u3002", "\u65E5", "\u00C5", "\u212B", "\u1E0A", "\uFB01", "\u2460", "I\u0307",
        "\u05D0", "\u05D1", "\u0627", "\u0661", "\u06F1", "\u0660", "\u202A", "\u202B", "\u202C", "\u202D", "\u202E", "\u2066", "\u2067", "\u2068", "\u2069", "\u200E", "\u200F", "\u061C",
        "(", ")", "[", "]", "{", "}", "\u300C", "\u300D", ",", ".", "-", "'", "\"", "!", "?", "%", "$", "/", ":", "0", "1", "a", "A", "\u0430", "\u03B1",
        "\U0001F600", "\U0001F468", "\U0001F3FB", "\U0001F1EB", "\U0001F1F7", "\U0001F44D", "\uFE0F", "\u20E3", "\U000E0067", "\U000E007F", "\U0001D400", "\U00010400",
        "\uD800", "\uDC00",
    ];

    private static readonly string[] HyphenatorTokens =
    [
        "1", "2", "3", "4", "5", ".", " ", "\n", "-", "%", "\\patterns{", "\\hyphenation{", "}", "{", "^^", "hy3ph", "he2n", "hen5at", "1tio", ".un1", "1ly.", "\u0301", "\u00E9", "\u00DF", "\u0130", "\U0001F600", "\u200D", "\uFEFF",
    ];

    // Words of the scripts that are written without spaces, from the unit tests of the dictionary
    private static readonly string[] ThaiWords =
    [
        "\u0E20\u0E32\u0E29\u0E32", "\u0E44\u0E17\u0E22", "\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35", "\u0E04\u0E23\u0E31\u0E1A", "\u0E09\u0E31\u0E19", "\u0E01\u0E34\u0E19", "\u0E02\u0E49\u0E32\u0E27",
        "\u0E15\u0E32", "\u0E15\u0E32\u0E01", "\u0E01\u0E25\u0E21", "\u0E25\u0E21", "\u0E19\u0E49\u0E33", "\u0E14\u0E35",
        "\u0E9E\u0EB2\u0EAA\u0EB2", "\u0EA5\u0EB2\u0EA7", "\u0EAA\u0EB0\u0E9A\u0EB2\u0E8D", "\u0E94\u0EB5",
        "\u1797\u17B6\u179F\u17B6", "\u1781\u17D2\u1798\u17C2\u179A",
        "\u1019\u103C\u1014\u103A\u1019\u102C", "\u1005\u102C", "\u1000\u103B\u1031\u1038\u1007\u1030\u1038", "\u1010\u1004\u103A", "\u1015\u102B", "\u1010\u101A\u103A",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("unicode-segmentation", Segmentation) { Seeds = GetSegmentationSeeds, Dictionary = TextTokens },
        new("unicode-normalization", Normalization) { Seeds = GetNormalizationSeeds, Dictionary = TextTokens },
        new("unicode-bidi", Bidi) { Seeds = GetBidiSeeds, Dictionary = TextTokens },
        new("unicode-confusables", Confusables) { Seeds = GetConfusableSeeds, Dictionary = [.. TextTokens, "\u0440", "\u0435", "\u043E", "\u0455", "\u217C", "\u0131", "rn", "m", "vv", "w", "paypal", "p\u0430\u0443\u0440\u0430l"] },
        new("unicode-hyphenator", Hyphenation) { Seeds = GetHyphenatorSeeds, Dictionary = HyphenatorTokens },
        new("unicode-line-break-dictionary", LineBreakDictionaryTarget) { Seeds = GetLineBreakDictionarySeeds, Dictionary = [.. TextTokens, .. ThaiWords, "#", "\t1"] },
        new("unicode-idna", Idna) { Seeds = GetIdnaSeeds, Dictionary = [.. TextTokens, "xn--", "\u3002", "\uFF0E", "\u00DF", "\u03C2", "\u1E9E", "\u2163", "_", "example", "com"] },
    ];

    /// <summary>Reads the code point at an index, as the library does: an unpaired surrogate is returned as is.</summary>
    private static int CodePointAt(string text, int index, out int length)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        length = 1;
        return c;
    }

    private static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            builder.Append((char)codePoint);
        }
        else
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
    }

    /// <summary>Determines whether an index is between the two halves of a surrogate pair.</summary>
    private static bool SplitsSurrogatePair(string text, int index) => index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);

    private static bool IsWellFormed(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsSurrogate(text[index]))
                continue;

            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>The ends of the grapheme clusters of a text, checked: contiguous, non-empty, covering the text, never inside a surrogate pair.</summary>
    private static bool[] GetGraphemeBoundaries(string text)
    {
        var boundaries = new bool[text.Length + 1];
        boundaries[0] = true;
        var expectedStart = 0;
        foreach (var cluster in Unicode.EnumerateGraphemeClusters(text))
        {
            var (start, end) = CheckSegment(text, cluster, expectedStart, "grapheme cluster");
            FuzzAssert.Equal(end - start, Unicode.GetGraphemeClusterLength(text.AsSpan(start)), $"The grapheme cluster at {start} does not have the length GetGraphemeClusterLength gives from its start");
            boundaries[end] = true;
            expectedStart = end;
        }

        FuzzAssert.Equal(text.Length, expectedStart, "The grapheme clusters do not cover the text");
        return boundaries;
    }

    private static (int Start, int End) CheckSegment(string text, Range range, int expectedStart, string kind)
    {
        FuzzAssert.False(range.Start.IsFromEnd || range.End.IsFromEnd, $"The {kind} {range} is relative to the end of the text");
        var start = range.Start.Value;
        var end = range.End.Value;
        FuzzAssert.Equal(expectedStart, start, $"The {kind} {range} does not start where the previous one ends");
        FuzzAssert.True(end > start && end <= text.Length, $"The {kind} {range} is empty or ends after the text ({text.Length} characters)");
        FuzzAssert.False(SplitsSurrogatePair(text, end), $"The {kind} {range} ends inside a surrogate pair");
        return (start, end);
    }

    private static string FormatPositions(IEnumerable<int> positions) => "[" + string.Join(", ", positions) + "]";

    /// <summary>Reads the lines of an embedded conformance file of the Unicode Character Database, without the comments and the blank lines.</summary>
    private static List<string> ReadConformanceLines(string resourceName)
    {
        using var stream = new GZipStream(FuzzResources.Open(typeof(UnicodeFuzzTargets).Assembly, resourceName), CompressionMode.Decompress);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('@', StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>Picks about <paramref name="count"/> lines spread over a conformance file.</summary>
    private static IEnumerable<(int LineIndex, string Line)> Sample(List<string> lines, int count)
    {
        var stride = Math.Max(1, lines.Count / count);
        for (var i = 0; i < lines.Count; i += stride)
        {
            yield return (i, lines[i]);
        }
    }

    /// <summary>Converts the hexadecimal code points of a field (<c>0044 0307</c>) to a text; the other tokens (the break and no-break signs) are ignored.</summary>
    private static string ParseCodePoints(string field)
    {
        var builder = new StringBuilder();
        foreach (var token in field.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) && codePoint is >= 0 and <= 0x10FFFF)
            {
                AppendCodePoint(builder, codePoint);
            }
        }

        return builder.ToString();
    }
}
