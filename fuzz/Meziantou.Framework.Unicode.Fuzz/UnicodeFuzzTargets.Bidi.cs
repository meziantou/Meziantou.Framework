using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    /// <summary>
    /// The levels of a paragraph are between the paragraph level and <c>MaxExplicitDepth + 1</c>, the two halves of a
    /// surrogate pair have the same level, the returned paragraph level is the one of <c>GetParagraphLevel</c>, the
    /// overload with a workspace (of the exact size, with garbage in it) gives the same levels, and a text for which
    /// <c>MayRequireBidi</c> is false has only level 0 when it is not right-to-left. After <c>ResetLineLevels</c>, the
    /// levels stay in range and <c>GetVisualOrder</c> is a permutation (the identity, or the reverse order, when every
    /// level is 0, or 1). The overload that takes the text gives the same order except that it keeps surrogate pairs in
    /// order, so a well-formed text stays well-formed.
    /// </summary>
    private static void Bidi(ReadOnlySpan<byte> data)
    {
        var direction = (UnicodeBidiDirection)(FuzzInput.ReadByte(ref data) % 3);
        var paragraph = FuzzText.Decode(data);

        // A paragraph separator, when there is one, is the last character of a paragraph (documented precondition)
        for (var index = 0; index < paragraph.Length;)
        {
            var codePoint = CodePointAt(paragraph, index, out var length);
            index += length;
            if (UnicodeProperties.GetBidiClass(codePoint) is UnicodeBidirectionalCategory.ParagraphSeparator)
            {
                paragraph = paragraph[..index];
                break;
            }
        }

        var levels = new byte[paragraph.Length];
        var paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, direction, levels);
        FuzzAssert.Equal(UnicodeBidi.GetParagraphLevel(paragraph, direction), paragraphLevel, $"ResolveLevels({direction}) and GetParagraphLevel disagree");
        switch (direction)
        {
            case UnicodeBidiDirection.LeftToRight:
                FuzzAssert.Equal(0, paragraphLevel, "The level of a left-to-right paragraph is not 0");
                break;
            case UnicodeBidiDirection.RightToLeft:
                FuzzAssert.Equal(1, paragraphLevel, "The level of a right-to-left paragraph is not 1");
                break;
        }

        var workspace = new int[UnicodeBidi.GetWorkspaceLength(paragraph.Length)];
        workspace.AsSpan().Fill(-123_456);
        var levels2 = new byte[paragraph.Length];
        levels2.AsSpan().Fill(0xAB);
        FuzzAssert.Equal(paragraphLevel, UnicodeBidi.ResolveLevels(paragraph, direction, levels2, workspace), "The overload with a workspace returns another paragraph level");
        FuzzAssert.True(levels.AsSpan().SequenceEqual(levels2), "The overload with a workspace resolves other levels");

        var mayRequireBidi = UnicodeBidi.MayRequireBidi(paragraph);
        for (var index = 0; index < levels.Length; index++)
        {
            FuzzAssert.True(levels[index] >= paragraphLevel && levels[index] <= UnicodeBidi.MaxExplicitDepth + 1, $"The level {levels[index]} at {index} is outside [{paragraphLevel}, {UnicodeBidi.MaxExplicitDepth + 1}]");
            if (SplitsSurrogatePair(paragraph, index))
            {
                FuzzAssert.Equal(levels[index - 1], levels[index], $"The two halves of the surrogate pair at {index - 1} have different levels");
            }

            if (!mayRequireBidi && direction is not UnicodeBidiDirection.RightToLeft)
            {
                FuzzAssert.Equal(0, (int)levels[index], $"MayRequireBidi is false but the level at {index} is not 0");
            }
        }

        UnicodeBidi.ResetLineLevels(paragraph, paragraphLevel, levels);
        var allZero = true;
        var allOne = true;
        foreach (var level in levels)
        {
            FuzzAssert.True(level >= paragraphLevel && level <= UnicodeBidi.MaxExplicitDepth + 1, $"ResetLineLevels gives the level {level}, outside [{paragraphLevel}, {UnicodeBidi.MaxExplicitDepth + 1}]");
            allZero &= level == 0;
            allOne &= level == 1;
        }

        var visualToLogical = new int[levels.Length];
        UnicodeBidi.GetVisualOrder(levels, visualToLogical);
        var seen = new bool[levels.Length];
        for (var position = 0; position < visualToLogical.Length; position++)
        {
            var logical = visualToLogical[position];
            FuzzAssert.True(logical >= 0 && logical < seen.Length && !seen[logical], $"GetVisualOrder is not a permutation: {logical} at {position}");
            seen[logical] = true;
            if (allZero)
            {
                FuzzAssert.Equal(position, logical, "GetVisualOrder reorders a line whose levels are all 0");
            }
            else if (allOne)
            {
                FuzzAssert.Equal(levels.Length - 1 - position, logical, "GetVisualOrder does not reverse a line whose levels are all 1");
            }
        }

        // The text overload is the same permutation, except that the two code units of a pair stay in order.
        var unitOrder = new int[paragraph.Length];
        UnicodeBidi.GetVisualOrder(paragraph, levels, unitOrder);
        for (var position = 0; position < unitOrder.Length; position++)
        {
            var logical = unitOrder[position];
            var expected = visualToLogical[position];
            if (logical != expected)
            {
                FuzzAssert.True(position + 1 < unitOrder.Length && expected == logical + 1 && visualToLogical[position + 1] == logical && unitOrder[position + 1] == expected, $"GetVisualOrder(line) differs from GetVisualOrder at {position} other than by a swapped pair");
                FuzzAssert.True(char.IsHighSurrogate(paragraph[logical]) && char.IsLowSurrogate(paragraph[expected]), $"GetVisualOrder(line) swaps {logical} and {expected}, which are not a surrogate pair");
                position++;
            }
        }

        if (IsWellFormed(paragraph))
        {
            var reordered = string.Concat(unitOrder.Select(index => paragraph[index]));
            FuzzAssert.True(IsWellFormed(reordered), "GetVisualOrder(line) gives an ill-formed text for a well-formed one");
        }
    }

    private static IEnumerable<FuzzSeed> GetBidiSeeds()
    {
        foreach (var (index, line) in Sample(ReadConformanceLines("BidiCharacterTest.txt.gz"), 300))
        {
            // Fields: code points; paragraph direction (0 = LTR, 1 = RTL, 2 = auto); resolved paragraph level; levels; order
            var fields = line.Split(';');
            var direction = fields[1].Trim() switch
            {
                "0" => UnicodeBidiDirection.LeftToRight,
                "1" => UnicodeBidiDirection.RightToLeft,
                _ => UnicodeBidiDirection.Auto,
            };

            yield return FuzzSeed.FromText($"BidiCharacterTest-{index}", [(byte)direction], ParseCodePoints(fields[0]));
        }

        foreach (var (name, text) in HandWrittenTexts)
        {
            yield return FuzzSeed.FromText(name, [(byte)UnicodeBidiDirection.Auto], text);
        }

        var deep = string.Concat(Enumerable.Repeat("\u202B\u202A\u2067", 50)) + "a\u05D0 1 " + string.Concat(Enumerable.Repeat("\u202C\u2069", 60));
        yield return FuzzSeed.FromText("deep-embeddings", [(byte)UnicodeBidiDirection.RightToLeft], deep);
        yield return FuzzSeed.FromText("brackets", [(byte)UnicodeBidiDirection.Auto], "\u05D0(a[b\u05D1]c)\u05D2 {\u0661\u0662} \u2068x\u2069");
    }
}
