using System.IO.Compression;

namespace Meziantou.Framework.Tests;

/// <summary>Runs the conformance test files of the Unicode Character Database (tests/files, see UnicodeConformance.LICENSE.md).</summary>
public sealed class UnicodeConformanceTests
{
    [Fact]
    public void GraphemeClusters_MatchGraphemeBreakTest()
    {
        var count = AssertBreaks("GraphemeBreakTest.txt.gz", text =>
        {
            var boundaries = new List<int> { 0 };
            foreach (var range in Unicode.EnumerateGraphemeClusters(text))
            {
                Assert.Equal(boundaries[^1], range.Start.Value);
                boundaries.Add(range.End.Value);
            }

            return boundaries;
        });

        Assert.True(count > 700, $"Only {count} cases were run");
    }

    [Fact]
    public void WordBoundaries_MatchWordBreakTest()
    {
        var count = AssertBreaks("WordBreakTest.txt.gz", text =>
        {
            var boundaries = new List<int> { 0 };
            foreach (var range in Unicode.EnumerateWordBoundaries(text))
            {
                Assert.Equal(boundaries[^1], range.Start.Value);
                boundaries.Add(range.End.Value);
            }

            return boundaries;
        });

        Assert.True(count > 1800, $"Only {count} cases were run");
    }

    [Fact]
    public void SentenceBoundaries_MatchSentenceBreakTest()
    {
        var count = AssertBreaks("SentenceBreakTest.txt.gz", text =>
        {
            var boundaries = new List<int> { 0 };
            foreach (var range in Unicode.EnumerateSentenceBoundaries(text))
            {
                Assert.Equal(boundaries[^1], range.Start.Value);
                boundaries.Add(range.End.Value);
            }

            return boundaries;
        });

        Assert.True(count > 500, $"Only {count} cases were run");
    }

    [Fact]
    public void LineBreaks_MatchLineBreakTest()
    {
        var count = AssertBreaks("LineBreakTest.txt.gz", text =>
        {
            var boundaries = new List<int>();
            foreach (var lineBreak in Unicode.EnumerateLineBreaks(text))
            {
                boundaries.Add(lineBreak.Index);
            }

            return boundaries;
        });

        Assert.True(count > 19000, $"Only {count} cases were run");
    }

    [Fact]
    public void Normalization_MatchesNormalizationTest()
    {
        var count = 0;
        var isFirstPart = false;
        var firstPartCodePoints = new HashSet<int>();
        var failures = new List<string>();
        foreach (var line in ReadLines("NormalizationTest.txt.gz"))
        {
            if (line.Length > 0 && line[0] == '@')
            {
                isFirstPart = line.StartsWith("@Part1", StringComparison.Ordinal);
                continue;
            }

            var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
            var columns = (commentIndex >= 0 ? line[..commentIndex] : line).Split(';');
            if (columns.Length < 5)
                continue;

            var c = new string[5];
            for (var i = 0; i < 5; i++)
            {
                var text = new StringBuilder();
                foreach (var token in columns[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    AppendCodePoint(text, int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }

                c[i] = text.ToString();
            }

            if (isFirstPart)
            {
                firstPartCodePoints.Add(char.ConvertToUtf32(c[0], 0));
            }

            count++;

            // The invariants listed in the header of the file.
            for (var i = 0; i < 5; i++)
            {
                Check(c[i], NormalizationForm.FormC, i < 3 ? c[1] : c[3]);
                Check(c[i], NormalizationForm.FormD, i < 3 ? c[2] : c[4]);
                Check(c[i], NormalizationForm.FormKC, c[3]);
                Check(c[i], NormalizationForm.FormKD, c[4]);
            }

            void Check(string source, NormalizationForm form, string expected)
            {
                var actual = UnicodeNormalization.Normalize(source, form);
                var isNormalized = string.Equals(source, expected, StringComparison.Ordinal);
                var buffer = new char[expected.Length + 4];
                if (!string.Equals(actual, expected, StringComparison.Ordinal)
                    || UnicodeNormalization.IsNormalized(source, form) != isNormalized
                    || ReferenceEquals(actual, source) != isNormalized
                    || UnicodeNormalization.GetNormalizedLength(source, form) != expected.Length
                    || !UnicodeNormalization.TryNormalize(source, buffer, out var written, form)
                    || !buffer.AsSpan(0, written).SequenceEqual(expected)
                    || (expected.Length > 0 && UnicodeNormalization.TryNormalize(source, buffer.AsSpan(0, expected.Length - 1), out _, form)))
                {
                    failures.Add($"{form} of [{Format(source)}]: expected [{Format(expected)}], actual [{Format(actual)}] ({line})");
                }
            }
        }

        // A code point that the first part does not list is unchanged by every form.
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF || firstPartCodePoints.Contains(codePoint))
                continue;

            var text = char.ConvertFromUtf32(codePoint);
            foreach (var form in (NormalizationForm[])[NormalizationForm.FormC, NormalizationForm.FormD, NormalizationForm.FormKC, NormalizationForm.FormKD])
            {
                if (!ReferenceEquals(text, UnicodeNormalization.Normalize(text, form)) || !UnicodeNormalization.IsNormalized(text, form))
                {
                    failures.Add($"U+{codePoint:X4} is not listed but {form} changes it");
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} failures over {count} cases:\n{string.Join('\n', failures.Take(20))}");
        }

        Assert.True(count > 19000, $"Only {count} cases were run");
        Assert.HasCountGreaterThan(17000, firstPartCodePoints);

        static string Format(string value) => string.Join(' ', value.EnumerateRunes().Select(rune => rune.Value.ToString("X4", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Bidi_MatchesBidiTest()
    {
        // The file gives sequences of classes, without characters: it exercises the rules other than N0.
        var classNames = new Dictionary<string, UnicodeBidirectionalCategory>(StringComparer.Ordinal)
        {
            ["L"] = UnicodeBidirectionalCategory.LeftToRight,
            ["R"] = UnicodeBidirectionalCategory.RightToLeft,
            ["AL"] = UnicodeBidirectionalCategory.RightToLeftArabic,
            ["EN"] = UnicodeBidirectionalCategory.EuropeanNumber,
            ["ES"] = UnicodeBidirectionalCategory.EuropeanSeparator,
            ["ET"] = UnicodeBidirectionalCategory.EuropeanTerminator,
            ["AN"] = UnicodeBidirectionalCategory.ArabicNumber,
            ["CS"] = UnicodeBidirectionalCategory.CommonSeparator,
            ["B"] = UnicodeBidirectionalCategory.ParagraphSeparator,
            ["S"] = UnicodeBidirectionalCategory.SegmentSeparator,
            ["WS"] = UnicodeBidirectionalCategory.WhiteSpace,
            ["ON"] = UnicodeBidirectionalCategory.OtherNeutral,
            ["NSM"] = UnicodeBidirectionalCategory.NonspacingMark,
            ["LRE"] = UnicodeBidirectionalCategory.LeftToRightEmbedding,
            ["LRO"] = UnicodeBidirectionalCategory.LeftToRightOverride,
            ["RLE"] = UnicodeBidirectionalCategory.RightToLeftEmbedding,
            ["RLO"] = UnicodeBidirectionalCategory.RightToLeftOverride,
            ["PDF"] = UnicodeBidirectionalCategory.PopDirectionalFormat,
            ["LRI"] = UnicodeBidirectionalCategory.LeftToRightIsolate,
            ["RLI"] = UnicodeBidirectionalCategory.RightToLeftIsolate,
            ["FSI"] = UnicodeBidirectionalCategory.FirstStrongIsolate,
            ["PDI"] = UnicodeBidirectionalCategory.PopDirectionalIsolate,
            ["BN"] = UnicodeBidirectionalCategory.BoundaryNeutral,
        };

        var count = 0;
        var failures = new List<string>();
        int[] expectedLevels = [];
        int[] expectedOrder = [];
        foreach (var line in ReadLines("BidiTest.txt.gz"))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line.StartsWith("@Levels:", StringComparison.Ordinal))
            {
                expectedLevels = ParseLevels(line["@Levels:".Length..]);
                continue;
            }

            if (line.StartsWith("@Reorder:", StringComparison.Ordinal))
            {
                expectedOrder = line["@Reorder:".Length..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                continue;
            }

            if (line[0] == '@')
                continue;

            var parts = line.Split(';');
            var classes = parts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(name => classNames[name]).ToArray();
            var directions = int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
            foreach (var (bit, direction) in ((int, UnicodeBidiDirection)[])[(1, UnicodeBidiDirection.Auto), (2, UnicodeBidiDirection.LeftToRight), (4, UnicodeBidiDirection.RightToLeft)])
            {
                if ((directions & bit) == 0)
                    continue;

                count++;
                var levels = new byte[classes.Length];
                var workspace = new int[UnicodeBidi.GetWorkspaceLength(classes.Length)];
                for (var i = 0; i < classes.Length; i++)
                {
                    workspace[i] = (int)classes[i];
                }

                var paragraphLevel = UnicodeBidi.ResolveLevelsCore(text: default, direction, levels, workspace);
                UnicodeBidi.ResetLineLevels(classes, paragraphLevel, levels);
                var error = Compare(levels, expectedLevels, expectedOrder);
                if (error is not null && failures.Count < 20)
                {
                    failures.Add($"{line} ({direction}): {error}");
                }
                else if (error is not null)
                {
                    failures.Add("");
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} of {count} cases of BidiTest.txt fail:\n{string.Join('\n', failures.Take(20))}");
        }

        Assert.True(count > 700000, $"Only {count} cases were run");
    }

    [Fact]
    public void Bidi_MatchesBidiCharacterTest()
    {
        var count = 0;
        var failures = new List<string>();
        foreach (var line in ReadLines("BidiCharacterTest.txt.gz"))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var fields = line.Split(';');
            var text = new StringBuilder();
            var starts = new List<int>();
            foreach (var token in fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                starts.Add(text.Length);
                AppendCodePoint(text, int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            var direction = fields[1] switch
            {
                "0" => UnicodeBidiDirection.LeftToRight,
                "1" => UnicodeBidiDirection.RightToLeft,
                _ => UnicodeBidiDirection.Auto,
            };

            var expectedParagraphLevel = int.Parse(fields[2], CultureInfo.InvariantCulture);
            var expectedLevels = ParseLevels(fields[3]);
            var expectedOrder = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();

            count++;
            var paragraph = text.ToString();
            var unitLevels = new byte[paragraph.Length];
            int paragraphLevel;
            if ((count & 1) == 0)
            {
                paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, direction, unitLevels);
            }
            else
            {
                // A workspace that is larger than needed and not cleared.
                var workspace = new int[UnicodeBidi.GetWorkspaceLength(paragraph.Length) + 3];
                workspace.AsSpan().Fill(0x5A5A5A5A);
                paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, direction, unitLevels, workspace);
            }

            UnicodeBidi.ResetLineLevels(paragraph, paragraphLevel, unitLevels);
            var levels = new byte[starts.Count];
            string? error = null;
            for (var i = 0; i < starts.Count; i++)
            {
                levels[i] = unitLevels[starts[i]];
                var end = i + 1 < starts.Count ? starts[i + 1] : paragraph.Length;
                if (end - starts[i] == 2 && unitLevels[starts[i] + 1] != levels[i])
                {
                    error = $"the two code units of the character {i} have different levels";
                }
            }

            if (paragraphLevel != expectedParagraphLevel || UnicodeBidi.GetParagraphLevel(paragraph, direction) != expectedParagraphLevel)
            {
                error = $"paragraph level {paragraphLevel}, expected {expectedParagraphLevel}";
            }

            error ??= Compare(levels, expectedLevels, expectedOrder);
            if (error is not null)
            {
                failures.Add(failures.Count < 20 ? $"{line}: {error}" : "");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} of {count} cases of BidiCharacterTest.txt fail:\n{string.Join('\n', failures.Take(20))}");
        }

        Assert.True(count > 90000, $"Only {count} cases were run");
    }

    [Fact]
    public void Idna_MatchesIdnaTestV2()
    {
        var transitionalOptions = new UnicodeIdnaOptions { TransitionalProcessing = true };
        var count = 0;
        var failures = new List<string>();
        foreach (var line in ReadLines("IdnaTestV2.txt.gz"))
        {
            var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
            var content = commentIndex >= 0 ? line[..commentIndex] : line;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            // source; toUnicode; toUnicodeStatus; toAsciiN; toAsciiNStatus; toAsciiT; toAsciiTStatus
            var fields = content.Split(';').Select(field => field.Trim()).ToArray();
            var source = UnescapeIdnaField(fields[0]);
            var expectedUnicode = fields[1].Length == 0 ? source : UnescapeIdnaField(fields[1]);
            var unicodeStatus = fields[2];
            var expectedAsciiN = fields[3].Length == 0 ? expectedUnicode : UnescapeIdnaField(fields[3]);
            var asciiNStatus = fields[4].Length == 0 ? unicodeStatus : fields[4];
            var expectedAsciiT = fields[5].Length == 0 ? expectedAsciiN : UnescapeIdnaField(fields[5]);
            var asciiTStatus = fields[6].Length == 0 ? asciiNStatus : fields[6];

            count++;
            string? error = null;
            var unicode = UnicodeIdna.ToUnicode(source, out var hasErrors);
            if (hasErrors != IsIdnaError(unicodeStatus))
            {
                error = $"ToUnicode reports errors={hasErrors}, expected {unicodeStatus}";
            }
            else if (!hasErrors && !string.Equals(unicode, expectedUnicode, StringComparison.Ordinal))
            {
                error = $"ToUnicode returns '{unicode}', expected '{expectedUnicode}'";
            }

            error ??= CompareToAscii(source, options: null, expectedAsciiN, asciiNStatus, "ToAsciiN");
            error ??= CompareToAscii(source, transitionalOptions, expectedAsciiT, asciiTStatus, "ToAsciiT");
            if (error is not null)
            {
                failures.Add(failures.Count < 20 ? $"{line}: {error}" : "");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} of {count} cases of IdnaTestV2.txt fail:\n{string.Join('\n', failures.Take(20))}");
        }

        Assert.True(count > 6000, $"Only {count} cases were run");

        static string? CompareToAscii(string source, UnicodeIdnaOptions? options, string expected, string status, string name)
        {
            var success = UnicodeIdna.TryToAscii(source, options, out var result);
            if (success == IsIdnaError(status))
                return $"{name} returns {success}, expected {status}";

            if (success && !string.Equals(result, expected, StringComparison.Ordinal))
                return $"{name} returns '{result}', expected '{expected}'";

            return null;
        }
    }

    /// <summary>A status of IdnaTestV2.txt: a list of error codes in brackets; <c>[]</c> means no error.</summary>
    private static bool IsIdnaError(string status) => status.Length > 0 && status != "[]";

    /// <summary>Unescapes a field of IdnaTestV2.txt: <c>\uXXXX</c>, <c>\x{XXXX}</c>, and <c>""</c> for the empty string.</summary>
    private static string UnescapeIdnaField(string value)
    {
        if (value == "\"\"")
            return "";

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 5 < value.Length && value[i + 1] == 'u')
            {
                AppendCodePoint(result, int.Parse(value.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 5;
            }
            else if (value[i] == '\\' && i + 2 < value.Length && value[i + 1] == 'x' && value[i + 2] == '{')
            {
                var end = value.IndexOf('}', i + 3, StringComparison.Ordinal);
                AppendCodePoint(result, int.Parse(value.AsSpan(i + 3, end - i - 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i = end;
            }
            else
            {
                result.Append(value[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>Parses the levels of a bidi test: -1 stands for a character the algorithm removes, whose level is not specified.</summary>
    private static int[] ParseLevels(string value)
    {
        return value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(level => level == "x" ? -1 : int.Parse(level, CultureInfo.InvariantCulture)).ToArray();
    }

    private static string? Compare(byte[] levels, int[] expectedLevels, int[] expectedOrder)
    {
        if (levels.Length != expectedLevels.Length)
            return $"{levels.Length} levels, expected {expectedLevels.Length}";

        var kept = new List<int>();
        for (var i = 0; i < levels.Length; i++)
        {
            if (expectedLevels[i] < 0)
                continue;

            kept.Add(i);
            if (levels[i] != expectedLevels[i])
                return $"levels {string.Join(' ', levels)}, expected {string.Join(' ', expectedLevels.Select(level => level < 0 ? "x" : level.ToString(CultureInfo.InvariantCulture)))}";
        }

        // The order is the one of the characters the algorithm keeps.
        var keptLevels = kept.Select(index => levels[index]).ToArray();
        var order = new int[keptLevels.Length];
        UnicodeBidi.GetVisualOrder(keptLevels, order);
        var actualOrder = order.Select(index => kept[index]).ToArray();
        return actualOrder.SequenceEqual(expectedOrder)
            ? null
            : $"order {string.Join(' ', actualOrder)}, expected {string.Join(' ', expectedOrder)}";
    }

    /// <summary>Runs a file of <c>÷ 0061 × 0308 ÷ 0062 ÷</c> lines: ÷ is a boundary, × is not.</summary>
    private static int AssertBreaks(string resourceName, Func<string, List<int>> getBoundaries)
    {
        var count = 0;
        var failures = new List<string>();
        foreach (var line in ReadLines(resourceName))
        {
            var (text, expected) = ParseBreakLine(line);
            if (text is null)
                continue;

            count++;
            var actual = getBoundaries(text);
            if (!expected.SequenceEqual(actual))
            {
                failures.Add($"{line}\n  expected: {string.Join(' ', expected)}\n  actual:   {string.Join(' ', actual)}");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} of {count} cases of {resourceName} fail:\n{string.Join('\n', failures.Take(20))}");
        }

        return count;
    }

    internal static (string? Text, List<int> Boundaries) ParseBreakLine(string line)
    {
        var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            line = line[..commentIndex];
        }

        var text = new StringBuilder();
        var boundaries = new List<int>();
        var hasContent = false;
        foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            hasContent = true;
            switch (token)
            {
                case "÷":
                    boundaries.Add(text.Length);
                    break;

                case "×":
                    break;

                default:
                    AppendCodePoint(text, int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    break;
            }
        }

        return hasContent ? (text.ToString(), boundaries) : (null, boundaries);
    }

    internal static void AppendCodePoint(StringBuilder destination, int codePoint)
    {
        // The files contain unpaired surrogates, which have no UTF-32 form.
        if (codePoint is >= 0xD800 and <= 0xDFFF)
        {
            destination.Append((char)codePoint);
        }
        else
        {
            destination.Append(char.ConvertFromUtf32(codePoint));
        }
    }

    internal static IEnumerable<string> ReadLines(string resourceName)
    {
        using var stream = typeof(UnicodeConformanceTests).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The resource '{resourceName}' is not embedded");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
