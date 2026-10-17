using System.Buffers;
using Bidi = Meziantou.Framework.UnicodeBidirectionalCategory;

namespace Meziantou.Framework;

/// <summary>Implements the Unicode Bidirectional Algorithm: the order in which text that mixes left-to-right and right-to-left scripts is displayed.</summary>
/// <remarks>
/// <para>
/// The algorithm is the one of Unicode Standard Annex #9 for <see cref="UnicodeProperties.UnicodeVersion"/>, whatever
/// the version of the runtime. It is applied in two steps, as the annex specifies: <see cref="ResolveLevels(ReadOnlySpan{char}, UnicodeBidiDirection, Span{byte})"/>
/// gives an embedding level to each UTF-16 code unit of a paragraph (rules P2 to I2); then, once the paragraph is
/// broken into lines, <see cref="ResetLineLevels(ReadOnlySpan{char}, int, Span{byte})"/> (rule L1) and <see cref="GetVisualOrder"/> (rule L2) give the
/// display order of each line. A character whose level is odd is displayed right to left.
/// </para>
/// <para>
/// Rule L3 (combining marks) is the responsibility of the code that shapes the text, and rule L4 (mirroring) is
/// available through <see cref="UnicodeProperties.TryGetBidiMirroringGlyph(int, out int)"/>.
/// </para>
/// </remarks>
/// <seealso href="https://www.unicode.org/reports/tr9/" />
public static class UnicodeBidi
{
    /// <summary>The greatest depth of explicit embeddings, overrides and isolates (<c>max_depth</c>). The greatest level is one more.</summary>
    public const int MaxExplicitDepth = 125;

    // The workspace holds, for each code unit: its current class, a link (see FindMatchingIsolates and
    // FindBracketPairs), and a slot of the list of the positions of the isolating run sequence being resolved.
    private const int WorkspaceFactor = 3;
    private const int StackallocLimit = 256;

    private const int MaxBracketDepth = 63;
    private const int NoLink = -1;

    // The link of an isolate initiator that has no matching PDI.
    private const int UnmatchedInitiator = int.MinValue;

    /// <summary>Determines whether a text can have a level other than 0.</summary>
    /// <param name="text">The text.</param>
    /// <returns>
    /// <see langword="false"/> when every level of the text is 0 for a paragraph direction that is
    /// <see cref="UnicodeBidiDirection.LeftToRight"/> or <see cref="UnicodeBidiDirection.Auto"/>: the text has no
    /// right-to-left character, no Arabic number and no explicit directional formatting character. The display order
    /// is then the order of the text.
    /// </returns>
    public static bool MayRequireBidi(ReadOnlySpan<char> text)
    {
        for (var index = 0; index < text.Length;)
        {
            // No character below U+0590 is right-to-left or is a directional formatting character.
            if (text[index] < 0x0590)
            {
                index++;
                continue;
            }

            var value = UnicodeProperties.GetBidiClass(Unicode.DecodeAt(text, index, out var length));
            if (value is Bidi.RightToLeft or Bidi.RightToLeftArabic or Bidi.ArabicNumber
                or Bidi.LeftToRightEmbedding or Bidi.LeftToRightOverride or Bidi.RightToLeftEmbedding or Bidi.RightToLeftOverride
                or Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate)
            {
                return true;
            }

            index += length;
        }

        return false;
    }

    /// <summary>Gets the embedding level of a paragraph.</summary>
    /// <param name="paragraph">The text of the paragraph.</param>
    /// <param name="direction">The direction of the paragraph.</param>
    /// <returns>
    /// 0 for a left-to-right paragraph, 1 for a right-to-left one. With <see cref="UnicodeBidiDirection.Auto"/>, the
    /// direction is the one of the first strong character that is not inside an isolate (rules P2 and P3), and
    /// left-to-right when there is none.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined value.</exception>
    public static int GetParagraphLevel(ReadOnlySpan<char> paragraph, UnicodeBidiDirection direction = UnicodeBidiDirection.Auto)
    {
        switch (direction)
        {
            case UnicodeBidiDirection.LeftToRight:
                return 0;

            case UnicodeBidiDirection.RightToLeft:
                return 1;

            case UnicodeBidiDirection.Auto:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "The value is not defined");
        }

        var depth = 0;
        for (var index = 0; index < paragraph.Length;)
        {
            var value = UnicodeProperties.GetBidiClass(Unicode.DecodeAt(paragraph, index, out var length));
            index += length;
            switch (value)
            {
                case Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate:
                    depth++;
                    break;

                case Bidi.PopDirectionalIsolate when depth > 0:
                    depth--;
                    break;

                case Bidi.LeftToRight when depth == 0:
                    return 0;

                case Bidi.RightToLeft or Bidi.RightToLeftArabic when depth == 0:
                    return 1;
            }
        }

        return 0;
    }

    /// <summary>Gets the size of the workspace <see cref="ResolveLevels(ReadOnlySpan{char}, UnicodeBidiDirection, Span{byte}, Span{int})"/> needs for a paragraph.</summary>
    /// <param name="textLength">The number of UTF-16 code units of the paragraph.</param>
    /// <returns>The number of elements of the workspace.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="textLength"/> is negative.</exception>
    /// <exception cref="OverflowException">The size does not fit in an <see cref="int"/>.</exception>
    public static int GetWorkspaceLength(int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);
        return checked(textLength * WorkspaceFactor);
    }

    /// <summary>Resolves the embedding level of each UTF-16 code unit of a paragraph.</summary>
    /// <param name="paragraph">The text of one paragraph. A paragraph separator, when there is one, is its last character.</param>
    /// <param name="direction">The direction of the paragraph.</param>
    /// <param name="levels">The buffer that receives one level per code unit of <paramref name="paragraph"/>.</param>
    /// <returns>The embedding level of the paragraph: 0 or 1.</returns>
    /// <remarks>
    /// <para>
    /// The two code units of a surrogate pair have the same level. The characters that the algorithm removes
    /// (explicit embedding and override characters, boundary neutrals) have the level of the character before them,
    /// so that they stay with it.
    /// </para>
    /// <para>This overload rents its workspace from the shared array pool when the paragraph is long.</para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="levels"/> is shorter than <paramref name="paragraph"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined value.</exception>
    public static int ResolveLevels(ReadOnlySpan<char> paragraph, UnicodeBidiDirection direction, Span<byte> levels)
    {
        var workspaceLength = GetWorkspaceLength(paragraph.Length);
        if (workspaceLength <= StackallocLimit * WorkspaceFactor)
        {
            Span<int> workspace = stackalloc int[StackallocLimit * WorkspaceFactor];
            return ResolveLevels(paragraph, direction, levels, workspace);
        }

        var rented = ArrayPool<int>.Shared.Rent(workspaceLength);
        try
        {
            return ResolveLevels(paragraph, direction, levels, rented);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rented);
        }
    }

    /// <summary>Resolves the embedding level of each UTF-16 code unit of a paragraph, using a workspace provided by the caller.</summary>
    /// <param name="paragraph">The text of one paragraph. A paragraph separator, when there is one, is its last character.</param>
    /// <param name="direction">The direction of the paragraph.</param>
    /// <param name="levels">The buffer that receives one level per code unit of <paramref name="paragraph"/>.</param>
    /// <param name="workspace">A buffer of at least <see cref="GetWorkspaceLength(int)"/> elements. Its content is overwritten.</param>
    /// <returns>The embedding level of the paragraph: 0 or 1.</returns>
    /// <remarks>See <see cref="ResolveLevels(ReadOnlySpan{char}, UnicodeBidiDirection, Span{byte})"/>. This overload does not allocate.</remarks>
    /// <exception cref="ArgumentException"><paramref name="levels"/> is shorter than <paramref name="paragraph"/>, or <paramref name="workspace"/> is too small.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined value.</exception>
    public static int ResolveLevels(ReadOnlySpan<char> paragraph, UnicodeBidiDirection direction, Span<byte> levels, Span<int> workspace)
    {
        if (direction is not (UnicodeBidiDirection.Auto or UnicodeBidiDirection.LeftToRight or UnicodeBidiDirection.RightToLeft))
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The value is not defined");

        if (levels.Length < paragraph.Length)
            throw new ArgumentException("The buffer is shorter than the paragraph", nameof(levels));

        if (workspace.Length < GetWorkspaceLength(paragraph.Length))
            throw new ArgumentException("The workspace is too small: see GetWorkspaceLength", nameof(workspace));

        var count = paragraph.Length;
        var classes = workspace[..count];
        for (var index = 0; index < count;)
        {
            classes[index] = (int)UnicodeProperties.GetBidiClass(Unicode.DecodeAt(paragraph, index, out var length));
            if (length == 2)
            {
                // The second code unit of a pair behaves as a removed character: it takes the level of the first.
                classes[index + 1] = (int)Bidi.BoundaryNeutral;
            }

            index += length;
        }

        return ResolveLevelsCore(paragraph, direction, levels[..count], workspace);
    }

    /// <summary>Resets the levels of the trailing white space of a line (rule L1).</summary>
    /// <param name="line">The text of the line.</param>
    /// <param name="paragraphLevel">The embedding level of the paragraph, as returned by <see cref="ResolveLevels(ReadOnlySpan{char}, UnicodeBidiDirection, Span{byte})"/>.</param>
    /// <param name="levels">The levels of the code units of <paramref name="line"/>, modified in place.</param>
    /// <remarks>
    /// The paragraph level is given to the segment and paragraph separators, to the white space and isolate
    /// formatting characters before them, and to those at the end of the line: white space at the end of a line
    /// always follows the direction of the paragraph. Call it for each line, after the paragraph is broken into
    /// lines and before <see cref="GetVisualOrder"/>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="levels"/> is shorter than <paramref name="line"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="paragraphLevel"/> is not 0 or 1.</exception>
    public static void ResetLineLevels(ReadOnlySpan<char> line, int paragraphLevel, Span<byte> levels)
    {
        if (paragraphLevel is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(paragraphLevel), paragraphLevel, "The level of a paragraph is 0 or 1");

        if (levels.Length < line.Length)
            throw new ArgumentException("The buffer is shorter than the line", nameof(levels));

        var resetStart = -1;
        for (var index = 0; index < line.Length;)
        {
            var value = UnicodeProperties.GetBidiClass(Unicode.DecodeAt(line, index, out var length));
            ResetLineLevel(value, index, length, ref resetStart, (byte)paragraphLevel, levels);
            index += length;
        }

        if (resetStart >= 0)
        {
            levels[resetStart..line.Length].Fill((byte)paragraphLevel);
        }
    }

    /// <summary>Rule L1 from the original classes of the characters of a line.</summary>
    internal static void ResetLineLevels(ReadOnlySpan<UnicodeBidirectionalCategory> line, int paragraphLevel, Span<byte> levels)
    {
        var resetStart = -1;
        for (var index = 0; index < line.Length; index++)
        {
            ResetLineLevel(line[index], index, 1, ref resetStart, (byte)paragraphLevel, levels);
        }

        if (resetStart >= 0)
        {
            levels[resetStart..line.Length].Fill((byte)paragraphLevel);
        }
    }

    private static void ResetLineLevel(Bidi value, int index, int length, ref int resetStart, byte paragraphLevel, Span<byte> levels)
    {
        if (value is Bidi.SegmentSeparator or Bidi.ParagraphSeparator)
        {
            levels[(resetStart >= 0 ? resetStart : index)..(index + length)].Fill(paragraphLevel);
            resetStart = -1;
        }
        else if (IsWhiteSpaceForLineEnd(value))
        {
            if (resetStart < 0)
            {
                resetStart = index;
            }
        }
        else
        {
            resetStart = -1;
        }
    }

    /// <summary>Computes the display order of the elements of a line from their levels (rule L2).</summary>
    /// <param name="levels">The levels of the elements of the line, in the order of the text. An element is a code unit, or anything coarser that has one level, such as a run of text.</param>
    /// <param name="visualToLogical">The buffer that receives, for each display position from left to right, the index of the element displayed there.</param>
    /// <exception cref="ArgumentException"><paramref name="visualToLogical"/> is shorter than <paramref name="levels"/>.</exception>
    public static void GetVisualOrder(ReadOnlySpan<byte> levels, Span<int> visualToLogical)
    {
        if (visualToLogical.Length < levels.Length)
            throw new ArgumentException("The buffer is shorter than the levels", nameof(visualToLogical));

        var highest = 0;
        var lowestOdd = int.MaxValue;
        for (var i = 0; i < levels.Length; i++)
        {
            visualToLogical[i] = i;
            int level = levels[i];
            highest = Math.Max(highest, level);
            if ((level & 1) != 0)
            {
                lowestOdd = Math.Min(lowestOdd, level);
            }
        }

        // From the highest level to the lowest odd level, reverse each sequence of elements at that level or higher.
        for (var level = highest; level >= lowestOdd; level--)
        {
            for (var i = 0; i < levels.Length;)
            {
                if (levels[i] < level)
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < levels.Length && levels[i] >= level)
                {
                    i++;
                }

                visualToLogical[start..i].Reverse();
            }
        }
    }

    /// <summary>Resolves the levels from the classes stored in the first part of the workspace.</summary>
    /// <param name="text">The text, read for the paired brackets; empty when only the classes are known.</param>
    /// <param name="direction">The direction of the paragraph.</param>
    /// <param name="levels">One level per class.</param>
    /// <param name="workspace">The workspace, whose first <c>levels.Length</c> elements are the classes.</param>
    internal static int ResolveLevelsCore(ReadOnlySpan<char> text, UnicodeBidiDirection direction, Span<byte> levels, Span<int> workspace)
    {
        var count = levels.Length;
        var classes = workspace[..count];
        var links = workspace.Slice(count, count);
        var sequence = workspace.Slice(count * 2, count);

        FindMatchingIsolates(classes, links);
        var paragraphLevel = direction switch
        {
            UnicodeBidiDirection.LeftToRight => 0,
            UnicodeBidiDirection.RightToLeft => 1,
            _ => FindFirstStrongLevel(classes, links, 0, count),
        };

        // The original classes are needed by N0 after the weak types are resolved; a character only has a class
        // other than its original one where X6 overrides it, and the brackets and marks N0 looks at are not removed.
        ResolveExplicitLevels(classes, links, levels, paragraphLevel);
        ResolveSequences(text, classes, links, sequence, levels, paragraphLevel);

        // The characters removed by X9 take the level of the character before them.
        var previous = (byte)paragraphLevel;
        for (var i = 0; i < count; i++)
        {
            if (classes[i] == (int)Bidi.BoundaryNeutral)
            {
                levels[i] = previous;
            }
            else
            {
                previous = levels[i];
            }
        }

        return paragraphLevel;
    }

    private static bool IsWhiteSpaceForLineEnd(Bidi value)
    {
        return value is Bidi.WhiteSpace or Bidi.FirstStrongIsolate or Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.PopDirectionalIsolate
            or Bidi.BoundaryNeutral or Bidi.LeftToRightEmbedding or Bidi.RightToLeftEmbedding or Bidi.LeftToRightOverride or Bidi.RightToLeftOverride
            or Bidi.PopDirectionalFormat;
    }

    private static bool IsIsolateInitiator(int value)
    {
        return (Bidi)value is Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate;
    }

    /// <summary>BD9: links each isolate initiator to its matching PDI, and each matched PDI to its initiator.</summary>
    private static void FindMatchingIsolates(ReadOnlySpan<int> classes, Span<int> links)
    {
        // While an initiator is open, its link is the index of the initiator that was open before it.
        var open = NoLink;
        for (var i = 0; i < classes.Length; i++)
        {
            links[i] = NoLink;
            if (IsIsolateInitiator(classes[i]))
            {
                links[i] = open;
                open = i;
            }
            else if ((Bidi)classes[i] is Bidi.PopDirectionalIsolate && open != NoLink)
            {
                var initiator = open;
                open = links[initiator];
                links[initiator] = i;
                links[i] = initiator;
            }
        }

        while (open != NoLink)
        {
            var initiator = open;
            open = links[initiator];
            links[initiator] = UnmatchedInitiator;
        }
    }

    /// <summary>P2 and P3: the level of the first strong character of a range that is not inside an isolate.</summary>
    private static int FindFirstStrongLevel(ReadOnlySpan<int> classes, ReadOnlySpan<int> links, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            switch ((Bidi)classes[i])
            {
                case Bidi.LeftToRight:
                    return 0;

                case Bidi.RightToLeft or Bidi.RightToLeftArabic:
                    return 1;

                case Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate:
                    // Skip to the matching PDI, or to the end when there is none.
                    if (links[i] < 0)
                        return 0;

                    i = links[i];
                    break;
            }
        }

        return 0;
    }

    /// <summary>X1 to X9: the explicit embedding levels, the overrides, and the removal of the formatting characters.</summary>
    private static void ResolveExplicitLevels(Span<int> classes, ReadOnlySpan<int> links, Span<byte> levels, int paragraphLevel)
    {
        const byte OverrideLeftToRight = 1;
        const byte OverrideRightToLeft = 2;
        const byte Isolate = 4;

        // The stack holds max_depth + 2 entries: the paragraph and one per valid embedding, override or isolate.
        Span<byte> stackLevels = stackalloc byte[MaxExplicitDepth + 2];
        Span<byte> stackStatus = stackalloc byte[MaxExplicitDepth + 2];
        var top = 0;
        stackLevels[0] = (byte)paragraphLevel;
        stackStatus[0] = 0;
        var overflowIsolates = 0;
        var overflowEmbeddings = 0;
        var validIsolates = 0;

        for (var i = 0; i < classes.Length; i++)
        {
            var value = (Bidi)classes[i];
            switch (value)
            {
                case Bidi.RightToLeftEmbedding or Bidi.LeftToRightEmbedding or Bidi.RightToLeftOverride or Bidi.LeftToRightOverride:
                    {
                        // X2 to X5
                        levels[i] = stackLevels[top];
                        var isRightToLeft = value is Bidi.RightToLeftEmbedding or Bidi.RightToLeftOverride;
                        var level = isRightToLeft ? (stackLevels[top] + 1) | 1 : (stackLevels[top] + 2) & ~1;
                        if (level <= MaxExplicitDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                        {
                            top++;
                            stackLevels[top] = (byte)level;
                            stackStatus[top] = value switch
                            {
                                Bidi.RightToLeftOverride => OverrideRightToLeft,
                                Bidi.LeftToRightOverride => OverrideLeftToRight,
                                _ => 0,
                            };
                        }
                        else if (overflowIsolates == 0)
                        {
                            overflowEmbeddings++;
                        }

                        // X9
                        classes[i] = (int)Bidi.BoundaryNeutral;
                        break;
                    }

                case Bidi.RightToLeftIsolate or Bidi.LeftToRightIsolate or Bidi.FirstStrongIsolate:
                    {
                        // X5a to X5c
                        levels[i] = stackLevels[top];
                        var isRightToLeft = value switch
                        {
                            Bidi.RightToLeftIsolate => true,
                            Bidi.LeftToRightIsolate => false,
                            _ => FindFirstStrongLevel(classes, links, i + 1, links[i] < 0 ? classes.Length : links[i]) == 1,
                        };

                        ApplyOverride(classes, i, stackStatus[top]);
                        var level = isRightToLeft ? (stackLevels[top] + 1) | 1 : (stackLevels[top] + 2) & ~1;
                        if (level <= MaxExplicitDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                        {
                            validIsolates++;
                            top++;
                            stackLevels[top] = (byte)level;
                            stackStatus[top] = Isolate;
                        }
                        else
                        {
                            overflowIsolates++;
                        }

                        break;
                    }

                case Bidi.PopDirectionalIsolate:
                    // X6a
                    if (overflowIsolates > 0)
                    {
                        overflowIsolates--;
                    }
                    else if (validIsolates > 0)
                    {
                        overflowEmbeddings = 0;
                        while ((stackStatus[top] & Isolate) == 0)
                        {
                            top--;
                        }

                        top--;
                        validIsolates--;
                    }

                    levels[i] = stackLevels[top];
                    ApplyOverride(classes, i, stackStatus[top]);
                    break;

                case Bidi.PopDirectionalFormat:
                    // X7
                    levels[i] = stackLevels[top];
                    if (overflowIsolates > 0)
                    {
                    }
                    else if (overflowEmbeddings > 0)
                    {
                        overflowEmbeddings--;
                    }
                    else if ((stackStatus[top] & Isolate) == 0 && top > 0)
                    {
                        top--;
                    }

                    // X9
                    classes[i] = (int)Bidi.BoundaryNeutral;
                    break;

                case Bidi.ParagraphSeparator:
                    // X8
                    levels[i] = (byte)paragraphLevel;
                    break;

                case Bidi.BoundaryNeutral:
                    levels[i] = stackLevels[top];
                    break;

                default:
                    // X6
                    levels[i] = stackLevels[top];
                    ApplyOverride(classes, i, stackStatus[top]);
                    break;
            }
        }

        static void ApplyOverride(Span<int> classes, int index, byte status)
        {
            if ((status & OverrideLeftToRight) != 0)
            {
                classes[index] = (int)Bidi.LeftToRight;
            }
            else if ((status & OverrideRightToLeft) != 0)
            {
                classes[index] = (int)Bidi.RightToLeft;
            }
        }
    }

    /// <summary>X10: resolves each isolating run sequence (W1 to W7, N0 to N2, I1 and I2).</summary>
    private static void ResolveSequences(ReadOnlySpan<char> text, Span<int> classes, Span<int> links, Span<int> sequence, Span<byte> levels, int paragraphLevel)
    {
        var count = classes.Length;
        for (var runStart = NextKept(classes, 0); runStart < count;)
        {
            var level = levels[runStart];
            var runEnd = FindRunEnd(classes, levels, runStart);

            // A level run that starts with a PDI matched to an initiator continues the sequence of that initiator.
            var isContinuation = links[runStart] >= 0 && links[runStart] < runStart;
            if (!isContinuation)
            {
                // BD13: gather the positions of the sequence.
                var length = 0;
                var start = runStart;
                var end = runEnd;
                int last;
                while (true)
                {
                    last = start;
                    for (var i = start; i < end; i++)
                    {
                        if (classes[i] != (int)Bidi.BoundaryNeutral)
                        {
                            sequence[length++] = i;
                            last = i;
                        }
                    }

                    // The run is followed by the one that starts with the PDI matching its last character.
                    if (links[last] <= last)
                        break;

                    start = links[last];
                    end = FindRunEnd(classes, levels, start);
                }

                // The types at the two ends of the sequence come from the higher of the two levels on each side.
                var before = PreviousKept(classes, runStart);
                var after = NextKept(classes, last + 1);
                var levelBefore = before < 0 ? paragraphLevel : levels[before];
                var levelAfter = after >= count || links[last] == UnmatchedInitiator ? paragraphLevel : levels[after];
                var startType = (Math.Max(level, levelBefore) & 1) != 0 ? Bidi.RightToLeft : Bidi.LeftToRight;
                var endType = (Math.Max(level, levelAfter) & 1) != 0 ? Bidi.RightToLeft : Bidi.LeftToRight;

                ResolveSequence(text, classes, links, sequence[..length], level, startType, endType);
            }

            runStart = NextKept(classes, runEnd);
        }

        // I1 and I2, once every sequence is resolved: the types at the ends of a sequence are computed from the
        // levels of the explicit rules, so the levels must not change before that.
        for (var i = 0; i < count; i++)
        {
            if (classes[i] != (int)Bidi.BoundaryNeutral)
            {
                levels[i] = (byte)(levels[i] + GetLevelIncrement(levels[i], (Bidi)classes[i]));
            }
        }
    }

    /// <summary>I1 and I2.</summary>
    private static int GetLevelIncrement(int level, Bidi value)
    {
        if ((level & 1) == 0)
        {
            return value switch
            {
                Bidi.RightToLeft => 1,
                Bidi.ArabicNumber or Bidi.EuropeanNumber => 2,
                _ => 0,
            };
        }

        return value is Bidi.LeftToRight or Bidi.EuropeanNumber or Bidi.ArabicNumber ? 1 : 0;
    }

    private static int NextKept(ReadOnlySpan<int> classes, int index)
    {
        while (index < classes.Length && classes[index] == (int)Bidi.BoundaryNeutral)
        {
            index++;
        }

        return index;
    }

    private static int PreviousKept(ReadOnlySpan<int> classes, int index)
    {
        index--;
        while (index >= 0 && classes[index] == (int)Bidi.BoundaryNeutral)
        {
            index--;
        }

        return index;
    }

    /// <summary>Finds the end of the level run that starts at a kept character: the removed characters do not end a run.</summary>
    private static int FindRunEnd(ReadOnlySpan<int> classes, ReadOnlySpan<byte> levels, int start)
    {
        var level = levels[start];
        var end = start + 1;
        for (var i = end; i < classes.Length; i++)
        {
            if (classes[i] == (int)Bidi.BoundaryNeutral)
                continue;

            if (levels[i] != level)
                break;

            end = i + 1;
        }

        return end;
    }

    private static void ResolveSequence(ReadOnlySpan<char> text, Span<int> classes, Span<int> links, ReadOnlySpan<int> sequence, int level, Bidi startType, Bidi endType)
    {
        // W1: a nonspacing mark takes the type of the character before it.
        var previous = startType;
        foreach (var index in sequence)
        {
            var value = (Bidi)classes[index];
            if (value is Bidi.NonspacingMark)
            {
                value = previous is Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate or Bidi.PopDirectionalIsolate
                    ? Bidi.OtherNeutral
                    : previous;
                classes[index] = (int)value;
            }

            previous = value;
        }

        // W2 and W3: a European number after an Arabic letter is an Arabic number; then an Arabic letter is R.
        var lastStrong = startType;
        foreach (var index in sequence)
        {
            switch ((Bidi)classes[index])
            {
                case Bidi.LeftToRight or Bidi.RightToLeft:
                    lastStrong = (Bidi)classes[index];
                    break;

                case Bidi.RightToLeftArabic:
                    lastStrong = Bidi.RightToLeftArabic;
                    classes[index] = (int)Bidi.RightToLeft;
                    break;

                case Bidi.EuropeanNumber when lastStrong is Bidi.RightToLeftArabic:
                    classes[index] = (int)Bidi.ArabicNumber;
                    break;
            }
        }

        // W4: a single separator between two numbers of the same type takes their type.
        for (var k = 1; k < sequence.Length - 1; k++)
        {
            var value = (Bidi)classes[sequence[k]];
            var left = (Bidi)classes[sequence[k - 1]];
            var right = (Bidi)classes[sequence[k + 1]];
            if (left != right)
                continue;

            if ((value is Bidi.EuropeanSeparator && left is Bidi.EuropeanNumber)
                || (value is Bidi.CommonSeparator && left is Bidi.EuropeanNumber or Bidi.ArabicNumber))
            {
                classes[sequence[k]] = (int)left;
            }
        }

        // W5: a sequence of European terminators next to a European number is part of the number.
        for (var k = 0; k < sequence.Length;)
        {
            if ((Bidi)classes[sequence[k]] is not Bidi.EuropeanTerminator)
            {
                k++;
                continue;
            }

            var start = k;
            while (k < sequence.Length && (Bidi)classes[sequence[k]] is Bidi.EuropeanTerminator)
            {
                k++;
            }

            if ((start > 0 && (Bidi)classes[sequence[start - 1]] is Bidi.EuropeanNumber)
                || (k < sequence.Length && (Bidi)classes[sequence[k]] is Bidi.EuropeanNumber))
            {
                for (var j = start; j < k; j++)
                {
                    classes[sequence[j]] = (int)Bidi.EuropeanNumber;
                }
            }
        }

        // W6 and W7: the other separators and terminators are neutral; a European number after L is L.
        lastStrong = startType;
        foreach (var index in sequence)
        {
            switch ((Bidi)classes[index])
            {
                case Bidi.EuropeanSeparator or Bidi.EuropeanTerminator or Bidi.CommonSeparator:
                    classes[index] = (int)Bidi.OtherNeutral;
                    break;

                case Bidi.LeftToRight or Bidi.RightToLeft:
                    lastStrong = (Bidi)classes[index];
                    break;

                case Bidi.EuropeanNumber when lastStrong is Bidi.LeftToRight:
                    classes[index] = (int)Bidi.LeftToRight;
                    break;
            }
        }

        var embedding = (level & 1) != 0 ? Bidi.RightToLeft : Bidi.LeftToRight;
        if (!text.IsEmpty)
        {
            ResolveBrackets(text, classes, links, sequence, embedding, startType);
        }

        // N1 and N2: a sequence of neutrals takes the direction of the text on both sides when they agree, and the
        // embedding direction otherwise.
        for (var k = 0; k < sequence.Length;)
        {
            if (!IsNeutral((Bidi)classes[sequence[k]]))
            {
                k++;
                continue;
            }

            var start = k;
            while (k < sequence.Length && IsNeutral((Bidi)classes[sequence[k]]))
            {
                k++;
            }

            var leading = start == 0 ? startType : GetStrongDirection((Bidi)classes[sequence[start - 1]]);
            var trailing = k == sequence.Length ? endType : GetStrongDirection((Bidi)classes[sequence[k]]);
            var resolved = leading == trailing ? leading : embedding;
            for (var j = start; j < k; j++)
            {
                classes[sequence[j]] = (int)resolved;
            }
        }
    }

    private static bool IsNeutral(Bidi value)
    {
        return value is Bidi.ParagraphSeparator or Bidi.SegmentSeparator or Bidi.WhiteSpace or Bidi.OtherNeutral
            or Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate or Bidi.PopDirectionalIsolate;
    }

    /// <summary>Gets the direction of a strong type, where the numbers count as R; another type has none.</summary>
    private static Bidi GetStrongDirection(Bidi value)
    {
        return value switch
        {
            Bidi.LeftToRight => Bidi.LeftToRight,
            Bidi.RightToLeft or Bidi.EuropeanNumber or Bidi.ArabicNumber => Bidi.RightToLeft,
            _ => Bidi.OtherNeutral,
        };
    }

    /// <summary>N0: gives a direction to the paired brackets of a sequence.</summary>
    private static void ResolveBrackets(ReadOnlySpan<char> text, Span<int> classes, Span<int> links, ReadOnlySpan<int> sequence, Bidi embedding, Bidi startType)
    {
        if (!FindBracketPairs(text, classes, links, sequence))
            return;

        // The pairs are visited in the order of their opening brackets.
        for (var open = 0; open < sequence.Length; open++)
        {
            var link = links[sequence[open]];
            if (link > -2 || link == UnmatchedInitiator)
                continue;

            var close = -(link + 2);
            links[sequence[open]] = NoLink;

            var hasOpposite = false;
            var hasEmbedding = false;
            for (var k = open + 1; k < close; k++)
            {
                var direction = GetStrongDirection((Bidi)classes[sequence[k]]);
                if (direction == embedding)
                {
                    hasEmbedding = true;
                    break;
                }

                hasOpposite |= direction is not Bidi.OtherNeutral;
            }

            Bidi resolved;
            if (hasEmbedding)
            {
                resolved = embedding;
            }
            else if (hasOpposite)
            {
                // The opposite direction is kept when the text before the pair establishes it.
                var context = startType;
                for (var k = open - 1; k >= 0; k--)
                {
                    var direction = GetStrongDirection((Bidi)classes[sequence[k]]);
                    if (direction is not Bidi.OtherNeutral)
                    {
                        context = direction;
                        break;
                    }
                }

                resolved = context == embedding ? embedding : context;
            }
            else
            {
                continue;
            }

            SetBracket(text, classes, sequence, open, resolved);
            SetBracket(text, classes, sequence, close, resolved);
        }
    }

    private static void SetBracket(ReadOnlySpan<char> text, Span<int> classes, ReadOnlySpan<int> sequence, int position, Bidi resolved)
    {
        classes[sequence[position]] = (int)resolved;

        // The nonspacing marks that follow a bracket take its direction.
        for (var k = position + 1; k < sequence.Length; k++)
        {
            var index = sequence[k];
            if (UnicodeProperties.GetBidiClass(Unicode.DecodeAt(text, index, out _)) is not Bidi.NonspacingMark)
                break;

            classes[index] = (int)resolved;
        }
    }

    /// <summary>BD16: stores in the link of each opening bracket of a pair the position of its closing bracket, as -(position + 2).</summary>
    /// <returns><see langword="false"/> when the sequence has no pair.</returns>
    private static bool FindBracketPairs(ReadOnlySpan<char> text, ReadOnlySpan<int> classes, Span<int> links, ReadOnlySpan<int> sequence)
    {
        Span<int> stackBrackets = stackalloc int[MaxBracketDepth];
        Span<int> stackPositions = stackalloc int[MaxBracketDepth];
        var depth = 0;
        var hasPair = false;
        for (var k = 0; k < sequence.Length; k++)
        {
            var index = sequence[k];
            if ((Bidi)classes[index] is not Bidi.OtherNeutral)
                continue;

            var codePoint = Unicode.DecodeAt(text, index, out _);
            var type = UnicodeProperties.GetBidiPairedBracketType(codePoint);
            if (type is UnicodeBidiPairedBracketType.None || !UnicodeProperties.TryGetBidiPairedBracket(codePoint, out var paired))
                continue;

            if (type is UnicodeBidiPairedBracketType.Open)
            {
                if (depth == MaxBracketDepth)
                {
                    // No pair at all for this sequence.
                    foreach (var position in sequence)
                    {
                        if (links[position] < NoLink && links[position] != UnmatchedInitiator)
                        {
                            links[position] = NoLink;
                        }
                    }

                    return false;
                }

                stackBrackets[depth] = GetCanonicalBracket(paired);
                stackPositions[depth] = k;
                depth++;
            }
            else
            {
                var closing = GetCanonicalBracket(codePoint);
                for (var entry = depth - 1; entry >= 0; entry--)
                {
                    if (stackBrackets[entry] != closing)
                        continue;

                    links[sequence[stackPositions[entry]]] = -(k + 2);
                    depth = entry;
                    hasPair = true;
                    break;
                }
            }
        }

        return hasPair;
    }

    /// <summary>Maps the two angle brackets that have a canonical equivalent to it, so that the equivalent brackets pair.</summary>
    private static int GetCanonicalBracket(int codePoint)
    {
        return codePoint switch
        {
            0x2329 => 0x3008,
            0x232A => 0x3009,
            _ => codePoint,
        };
    }
}
