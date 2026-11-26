using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bidi = Meziantou.Framework.UnicodeBidirectionalCategory;

namespace Meziantou.Framework;

/// <summary>Implements the Unicode Bidirectional Algorithm: the order in which text that mixes left-to-right and right-to-left scripts is displayed.</summary>
/// <remarks>
/// <para>
/// The algorithm is the one of Unicode Standard Annex #9 for <see cref="UnicodeProperties.UnicodeVersion"/>, whatever
/// the version of the runtime. It is applied in two steps, as the annex specifies: <see cref="ResolveLevels(ReadOnlySpan{char}, UnicodeBidiDirection, Span{byte})"/>
/// gives an embedding level to each UTF-16 code unit of a paragraph (rules P2 to I2); then, once the paragraph is
/// broken into lines, <see cref="ResetLineLevels(ReadOnlySpan{char}, int, Span{byte})"/> (rule L1) and <see cref="GetVisualOrder(ReadOnlySpan{char}, ReadOnlySpan{byte}, Span{int})"/> (rule L2) give the
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

    // The workspace holds 3 ints per code unit. The bytes of the first ones hold the current class of each code unit,
    // its properties (see UnicodeTables.GetBidi), and the types of the isolating run sequence being resolved. The
    // second ones hold a link per code unit (see FindMatchingIsolates and FindBracketPairs), and the third ones the
    // positions of the characters of the sequence.
    private const int WorkspaceFactor = 3;
    private const int StackallocLimit = 64; // The workspace stays under 1 KB on the stack

    private const int MaxBracketDepth = 63;
    private const int NoLink = -1;

    // The link of an isolate initiator that has no matching PDI.
    private const int UnmatchedInitiator = int.MinValue;

    // The bits of the value of UnicodeTables.GetBidi.
    private const int ClassBits = 0x1F;
    private const int BracketTypeShift = 5;
    private const int BracketTypeBits = 0x60;

    // A mask of classes has the bit 1 << class of each class it contains, and HasBrackets when a character is a paired bracket.
    private const int HasBrackets = BracketTypeBits << 18;
    private const int ExplicitClasses = (1 << (int)Bidi.LeftToRightEmbedding) | (1 << (int)Bidi.LeftToRightOverride) | (1 << (int)Bidi.RightToLeftEmbedding)
        | (1 << (int)Bidi.RightToLeftOverride) | (1 << (int)Bidi.PopDirectionalFormat) | IsolateClasses;

    private const int IsolateClasses = (1 << (int)Bidi.LeftToRightIsolate) | (1 << (int)Bidi.RightToLeftIsolate) | (1 << (int)Bidi.FirstStrongIsolate)
        | (1 << (int)Bidi.PopDirectionalIsolate);

    private const int NeutralClasses = (1 << (int)Bidi.ParagraphSeparator) | (1 << (int)Bidi.SegmentSeparator) | (1 << (int)Bidi.WhiteSpace)
        | (1 << (int)Bidi.OtherNeutral) | IsolateClasses;

    private const int SeparatorClasses = (1 << (int)Bidi.EuropeanSeparator) | (1 << (int)Bidi.CommonSeparator);

    // I1 and I2: two bits per class, the increment of the level of a character of that type at an even level and at an odd level.
    private const ulong EvenLevelIncrements = (1UL << (2 * (int)Bidi.RightToLeft)) | (2UL << (2 * (int)Bidi.ArabicNumber)) | (2UL << (2 * (int)Bidi.EuropeanNumber));
    private const ulong OddLevelIncrements = (1UL << (2 * (int)Bidi.LeftToRight)) | (1UL << (2 * (int)Bidi.ArabicNumber)) | (1UL << (2 * (int)Bidi.EuropeanNumber));

    // The properties of U+0000 to U+07FF, which hold the scripts written from right to left that are the most used
    // (Hebrew, Arabic, Syriac, Thaana, NKo), read faster than through the tables of all the code points.
    private static readonly byte[] LowProperties = CreateLowProperties();

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
            // No character below U+0590 is right-to-left or is a directional formatting character: skip them with a
            // vectorized search.
            if (text[index] < 0x0590)
            {
                var next = text[index..].IndexOfAnyExceptInRange('\0', '\u058F');
                if (next < 0)
                    return false;

                index += next;
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
            Span<int> workspace = stackalloc int[workspaceLength];
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
        return ResolveLevels(paragraph, direction, levels, workspace, allowFastPath: true);
    }

    /// <summary>Resolves the embedding level of each UTF-16 code unit of a paragraph.</summary>
    /// <param name="paragraph">The text of one paragraph.</param>
    /// <param name="direction">The direction of the paragraph.</param>
    /// <param name="levels">The buffer that receives one level per code unit of <paramref name="paragraph"/>.</param>
    /// <param name="workspace">A buffer of at least <see cref="GetWorkspaceLength(int)"/> elements.</param>
    /// <param name="allowFastPath"><see langword="false"/> to resolve a paragraph without explicit formatting characters with the general path, to compare the two in tests.</param>
    internal static int ResolveLevels(ReadOnlySpan<char> paragraph, UnicodeBidiDirection direction, Span<byte> levels, Span<int> workspace, bool allowFastPath)
    {
        if (direction is not (UnicodeBidiDirection.Auto or UnicodeBidiDirection.LeftToRight or UnicodeBidiDirection.RightToLeft))
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The value is not defined");

        if (levels.Length < paragraph.Length)
            throw new ArgumentException("The buffer is shorter than the paragraph", nameof(levels));

        if (workspace.Length < GetWorkspaceLength(paragraph.Length))
            throw new ArgumentException("The workspace is too small: see GetWorkspaceLength", nameof(workspace));

        var count = paragraph.Length;

        // Without right-to-left characters, Arabic numbers and explicit directional formatting characters, the
        // paragraph is left-to-right and every level is 0: European numbers become L after L (rule W7).
        if (direction is not UnicodeBidiDirection.RightToLeft && !MayRequireBidi(paragraph))
        {
            levels[..count].Clear();
            return 0;
        }

        // The properties of each code unit, and the sequence of the paragraph: the types and positions of the
        // characters other than the boundary neutrals, which are removed (X9). When the paragraph has no explicit
        // formatting character, this sequence is its only isolating run sequence.
        var bytes = GetBytes(workspace[..count]);
        var properties = bytes.Slice(count, count);
        var sequenceTypes = bytes.Slice(count * 2, count);
        var positions = workspace.Slice(count * 2, count);
        var length = ReadParagraph(paragraph, properties, sequenceTypes, positions, out var classMask);
        return ResolveLevelsCore(paragraph, direction, levels[..count], workspace, length, classMask, allowFastPath);
    }

    /// <summary>Reads the properties of the code units of a paragraph, and the types and positions of its characters that are not boundary neutrals.</summary>
    /// <returns>The number of characters that are not boundary neutrals.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadParagraph(ReadOnlySpan<char> paragraph, Span<byte> properties, Span<byte> sequenceTypes, Span<int> positions, out int classMask)
    {
        var mask = 0;
        var length = 0;
        var lowProperties = LowProperties;
        for (var index = 0; index < paragraph.Length; index++)
        {
            var c = paragraph[index];
            int value;
            var isPair = false;
            if (c < lowProperties.Length)
            {
                value = lowProperties[c];
            }
            else if (!char.IsSurrogate(c))
            {
                value = UnicodeTables.GetBidi(c);
            }
            else if (char.IsHighSurrogate(c) && index + 1 < paragraph.Length && char.IsLowSurrogate(paragraph[index + 1]))
            {
                // The second code unit of a pair behaves as a removed character: it takes the level of the first.
                value = UnicodeTables.GetBidi(char.ConvertToUtf32(c, paragraph[index + 1]));
                properties[index + 1] = (byte)Bidi.BoundaryNeutral;
                mask |= 1 << (int)Bidi.BoundaryNeutral;
                isPair = true;
            }
            else
            {
                value = UnicodeTables.GetBidi(c);
            }

            var bidiClass = value & ClassBits;
            properties[index] = (byte)value;
            mask |= (1 << bidiClass) | ((value & BracketTypeBits) << 18);

            // length <= index, so the slots exist; a removed character is overwritten by the next one.
            positions[length] = index;
            sequenceTypes[length] = (byte)bidiClass;
            length += bidiClass != (int)Bidi.BoundaryNeutral ? 1 : 0;
            if (isPair)
            {
                index++;
            }
        }

        classMask = mask;
        return length;
    }

    /// <summary>Gets the bytes of the first part of the workspace, which holds the classes, the properties and the types of a sequence.</summary>
    private static Span<byte> GetBytes(Span<int> workspace)
    {
        // Reading and writing the bytes of an int is always valid.
        return unsafe(MemoryMarshal.AsBytes(workspace));
    }

    private static byte[] CreateLowProperties()
    {
        var result = new byte[0x800];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (byte)UnicodeTables.GetBidi(i);
        }

        return result;
    }

    /// <summary>Resolves the levels of a paragraph given by the classes of its characters, without resolving the paired brackets (rule N0).</summary>
    internal static int ResolveLevelsFromClasses(ReadOnlySpan<Bidi> classes, UnicodeBidiDirection direction, Span<byte> levels, bool allowFastPath)
    {
        var count = classes.Length;
        var workspace = new int[GetWorkspaceLength(count)];
        var bytes = GetBytes(workspace.AsSpan(0, count));
        var positions = workspace.AsSpan(count * 2, count);
        var classMask = 0;
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            var value = classes[i];
            bytes[count + i] = (byte)value;
            classMask |= 1 << (int)value;
            if (value is not Bidi.BoundaryNeutral)
            {
                positions[length] = i;
                bytes[(count * 2) + length] = (byte)value;
                length++;
            }
        }

        return ResolveLevelsCore(text: default, direction, levels[..count], workspace, length, classMask, allowFastPath);
    }

    /// <summary>Resets the levels of the trailing white space of a line (rule L1).</summary>
    /// <param name="line">The text of the line.</param>
    /// <param name="paragraphLevel">The embedding level of the paragraph, as returned by <see cref="ResolveLevels(ReadOnlySpan{char}, UnicodeBidiDirection, Span{byte})"/>.</param>
    /// <param name="levels">The levels of the code units of <paramref name="line"/>, modified in place.</param>
    /// <remarks>
    /// The paragraph level is given to the segment and paragraph separators, to the white space and isolate
    /// formatting characters before them, and to those at the end of the line: white space at the end of a line
    /// always follows the direction of the paragraph. Call it for each line, after the paragraph is broken into
    /// lines and before <see cref="GetVisualOrder(ReadOnlySpan{char}, ReadOnlySpan{byte}, Span{int})"/>.
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
    /// <param name="levels">The levels of the elements of the line, in the order of the text. An element is a code unit, a code point, or anything coarser that has one level, such as a run of text.</param>
    /// <param name="visualToLogical">The buffer that receives, for each display position from left to right, the index of the element displayed there.</param>
    /// <remarks>
    /// The elements are reversed independently of each other. When they are the UTF-16 code units of a text, the two
    /// code units of a surrogate pair at an odd level are reversed too: use
    /// <see cref="GetVisualOrder(ReadOnlySpan{char}, ReadOnlySpan{byte}, Span{int})"/>, which keeps them in order.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="visualToLogical"/> is shorter than <paramref name="levels"/>.</exception>
    public static void GetVisualOrder(ReadOnlySpan<byte> levels, Span<int> visualToLogical)
    {
        if (visualToLogical.Length < levels.Length)
            throw new ArgumentException("The buffer is shorter than the levels", nameof(visualToLogical));

        Reorder(text: default, levels, visualToLogical[..levels.Length]);
    }

    /// <summary>Computes the display order of the UTF-16 code units of a line from their levels (rule L2).</summary>
    /// <param name="line">The text of the line.</param>
    /// <param name="levels">The levels of the code units of <paramref name="line"/>, as given by <see cref="ResolveLevels(ReadOnlySpan{char}, UnicodeBidiDirection, Span{byte})"/> and <see cref="ResetLineLevels(ReadOnlySpan{char}, int, Span{byte})"/>.</param>
    /// <param name="visualToLogical">The buffer that receives, for each display position from left to right, the index of the code unit displayed there.</param>
    /// <remarks>
    /// The two code units of a surrogate pair stay in the order of the text, whatever their level: applying the
    /// order to a well-formed text gives a well-formed text. A pair has the level of its first code unit.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="levels"/> or <paramref name="visualToLogical"/> is shorter than <paramref name="line"/>.</exception>
    public static void GetVisualOrder(ReadOnlySpan<char> line, ReadOnlySpan<byte> levels, Span<int> visualToLogical)
    {
        if (levels.Length < line.Length)
            throw new ArgumentException("The buffer is shorter than the line", nameof(levels));

        if (visualToLogical.Length < line.Length)
            throw new ArgumentException("The buffer is shorter than the line", nameof(visualToLogical));

        visualToLogical = visualToLogical[..line.Length];
        Reorder(line, levels[..line.Length], visualToLogical);

        // Both code units of a pair have the same level, so they are reversed together: a reversed pair is adjacent.
        for (var i = 0; i + 1 < visualToLogical.Length; i++)
        {
            var first = visualToLogical[i];
            var second = visualToLogical[i + 1];
            if (first == second + 1 && IsPairStart(line, second))
            {
                visualToLogical[i] = second;
                visualToLogical[i + 1] = first;
                i++;
            }
        }
    }

    /// <summary>Rule L2. When <paramref name="text"/> is not empty, the second code unit of a pair takes the level of the first.</summary>
    private static void Reorder(ReadOnlySpan<char> text, ReadOnlySpan<byte> levels, Span<int> visualToLogical)
    {
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
                if (GetLevel(text, levels, i) < level)
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < levels.Length && GetLevel(text, levels, i) >= level)
                {
                    i++;
                }

                visualToLogical[start..i].Reverse();
            }
        }

        static byte GetLevel(ReadOnlySpan<char> text, ReadOnlySpan<byte> levels, int index)
        {
            return index > 0 && index < text.Length && IsPairStart(text, index - 1) ? levels[index - 1] : levels[index];
        }
    }

    private static bool IsPairStart(ReadOnlySpan<char> text, int index)
    {
        return char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
    }

    /// <summary>Resolves the levels from the properties and the sequence of the paragraph, stored in the workspace.</summary>
    /// <param name="text">The text, read for the paired brackets; empty when only the classes are known.</param>
    /// <param name="direction">The direction of the paragraph.</param>
    /// <param name="levels">One level per code unit.</param>
    /// <param name="workspace">
    /// The workspace. The bytes of its first <c>levels.Length</c> elements hold the classes, then the properties of
    /// each code unit, then the types of the characters that are not boundary neutrals. Its last <c>levels.Length</c>
    /// elements hold the positions of these characters.
    /// </param>
    /// <param name="sequenceLength">The number of characters that are not boundary neutrals.</param>
    /// <param name="classMask">The mask of the classes of the paragraph (see <see cref="HasBrackets"/>).</param>
    /// <param name="allowFastPath"><see langword="false"/> to use the general path even when the paragraph has no explicit formatting characters.</param>
    private static int ResolveLevelsCore(ReadOnlySpan<char> text, UnicodeBidiDirection direction, Span<byte> levels, Span<int> workspace, int sequenceLength, int classMask, bool allowFastPath)
    {
        var count = levels.Length;
        var bytes = GetBytes(workspace[..count]);
        var classes = bytes[..count];
        var properties = bytes.Slice(count, count);
        var sequenceTypes = bytes.Slice(count * 2, count);
        var links = workspace.Slice(count, count);
        var positions = workspace.Slice(count * 2, count);

        if (allowFastPath && (classMask & ExplicitClasses) == 0)
            return ResolveImplicitParagraph(text, direction, properties, sequenceTypes[..sequenceLength], links, positions[..sequenceLength], levels, classMask);

        for (var i = 0; i < classes.Length; i++)
        {
            classes[i] = (byte)(properties[i] & ClassBits);
        }

        if ((classMask & IsolateClasses) != 0)
        {
            FindMatchingIsolates(classes, links);
        }
        else
        {
            links.Fill(NoLink);
        }

        var paragraphLevel = direction switch
        {
            UnicodeBidiDirection.LeftToRight => 0,
            UnicodeBidiDirection.RightToLeft => 1,
            _ => FindFirstStrongLevel(classes, links, 0, count),
        };

        // The original classes are needed by N0 after the weak types are resolved: they are the properties, which
        // the explicit rules do not change.
        ResolveExplicitLevels(classes, links, levels, paragraphLevel);
        ResolveSequences(text, classes, properties, sequenceTypes, links, positions, levels, paragraphLevel, classMask);

        // I1 and I2, once every sequence is resolved: the types at the ends of a sequence are computed from the
        // levels of the explicit rules, so the levels must not change before that. The characters removed by X9 take
        // the level of the character before them.
        var previous = (byte)paragraphLevel;
        for (var i = 0; i < count; i++)
        {
            var value = classes[i];
            if (value != (byte)Bidi.BoundaryNeutral)
            {
                previous = (byte)(levels[i] + GetLevelIncrement(levels[i], (Bidi)value));
            }

            levels[i] = previous;
        }

        return paragraphLevel;
    }

    /// <summary>Resolves a paragraph that has no explicit formatting character.</summary>
    /// <remarks>
    /// Rules X1 to X9 give the paragraph level to every character and change no class, other than removing the
    /// boundary neutrals, so the paragraph is one level run and one isolating run sequence, whose start and end types
    /// are the direction of the paragraph.
    /// </remarks>
    private static int ResolveImplicitParagraph(ReadOnlySpan<char> text, UnicodeBidiDirection direction, ReadOnlySpan<byte> properties, Span<byte> sequenceTypes, Span<int> links, ReadOnlySpan<int> positions, Span<byte> levels, int classMask)
    {
        // Without isolate initiators, FindFirstStrongLevel does not read the links.
        var paragraphLevel = direction switch
        {
            UnicodeBidiDirection.LeftToRight => 0,
            UnicodeBidiDirection.RightToLeft => 1,
            _ => FindFirstStrongLevel(sequenceTypes, links, 0, sequenceTypes.Length),
        };

        var type = paragraphLevel == 1 ? Bidi.RightToLeft : Bidi.LeftToRight;
        ResolveSequence(text, sequenceTypes, properties, links, positions, paragraphLevel, type, type, classMask);

        SetImplicitLevels(sequenceTypes, positions, levels, paragraphLevel);
        return paragraphLevel;
    }

    /// <summary>I1 and I2 for a paragraph that has no explicit formatting character. The removed characters take the level of the character before them.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetImplicitLevels(ReadOnlySpan<byte> sequenceTypes, ReadOnlySpan<int> positions, Span<byte> levels, int paragraphLevel)
    {
        var increments = paragraphLevel == 0 ? EvenLevelIncrements : OddLevelIncrements;
        if (sequenceTypes.Length == levels.Length)
        {
            // No character is removed: the position of each character is its index in the sequence.
            for (var k = 0; k < sequenceTypes.Length; k++)
            {
                levels[k] = (byte)(paragraphLevel + (int)((increments >> (sequenceTypes[k] * 2)) & 3));
            }

            return;
        }

        var previous = (byte)paragraphLevel;
        var next = 0;
        for (var k = 0; k < positions.Length; k++)
        {
            var position = positions[k];
            while (next < position)
            {
                levels[next] = previous;
                next++;
            }

            previous = (byte)(paragraphLevel + (int)((increments >> (sequenceTypes[k] * 2)) & 3));
            levels[position] = previous;
            next = position + 1;
        }

        levels[next..].Fill(previous);
    }

    private static bool IsWhiteSpaceForLineEnd(Bidi value)
    {
        return value is Bidi.WhiteSpace or Bidi.FirstStrongIsolate or Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.PopDirectionalIsolate
            or Bidi.BoundaryNeutral or Bidi.LeftToRightEmbedding or Bidi.RightToLeftEmbedding or Bidi.LeftToRightOverride or Bidi.RightToLeftOverride
            or Bidi.PopDirectionalFormat;
    }

    private static bool IsIsolateInitiator(byte value)
    {
        return (Bidi)value is Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate;
    }

    /// <summary>BD9: links each isolate initiator to its matching PDI, and each matched PDI to its initiator.</summary>
    private static void FindMatchingIsolates(ReadOnlySpan<byte> classes, Span<int> links)
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
    private static int FindFirstStrongLevel(ReadOnlySpan<byte> classes, ReadOnlySpan<int> links, int start, int end)
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
    private static void ResolveExplicitLevels(Span<byte> classes, ReadOnlySpan<int> links, Span<byte> levels, int paragraphLevel)
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
                        classes[i] = (byte)Bidi.BoundaryNeutral;
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
                    classes[i] = (byte)Bidi.BoundaryNeutral;
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

        static void ApplyOverride(Span<byte> classes, int index, byte status)
        {
            if ((status & OverrideLeftToRight) != 0)
            {
                classes[index] = (byte)Bidi.LeftToRight;
            }
            else if ((status & OverrideRightToLeft) != 0)
            {
                classes[index] = (byte)Bidi.RightToLeft;
            }
        }
    }

    /// <summary>X10: resolves the types of each isolating run sequence (W1 to W7, N0 to N2).</summary>
    private static void ResolveSequences(ReadOnlySpan<char> text, Span<byte> classes, ReadOnlySpan<byte> properties, Span<byte> sequenceTypes, Span<int> links, Span<int> positions, Span<byte> levels, int paragraphLevel, int classMask)
    {
        var count = classes.Length;
        for (var runStart = NextKept(classes, 0); runStart < count;)
        {
            // A level run that starts with a PDI matched to an initiator continues the sequence of that initiator,
            // which is already resolved.
            if (links[runStart] >= 0 && links[runStart] < runStart)
            {
                runStart = NextKept(classes, FindRunEnd(classes, levels, runStart));
                continue;
            }

            // BD13: gather the positions and the types of the sequence.
            var level = levels[runStart];
            var length = 0;
            var runEnd = GatherRun(classes, levels, runStart, positions, sequenceTypes, ref length);
            var last = positions[length - 1];
            while (links[last] > last)
            {
                // The run is followed by the one that starts with the PDI matching its last character.
                GatherRun(classes, levels, links[last], positions, sequenceTypes, ref length);
                last = positions[length - 1];
            }

            // The types at the two ends of the sequence come from the higher of the two levels on each side.
            var before = PreviousKept(classes, runStart);
            var after = NextKept(classes, last + 1);
            var levelBefore = before < 0 ? paragraphLevel : levels[before];
            var levelAfter = after >= count || links[last] == UnmatchedInitiator ? paragraphLevel : levels[after];
            var startType = (Math.Max(level, levelBefore) & 1) != 0 ? Bidi.RightToLeft : Bidi.LeftToRight;
            var endType = (Math.Max(level, levelAfter) & 1) != 0 ? Bidi.RightToLeft : Bidi.LeftToRight;

            ResolveSequence(text, sequenceTypes[..length], properties, links, positions[..length], level, startType, endType, classMask);
            for (var k = 0; k < length; k++)
            {
                classes[positions[k]] = sequenceTypes[k];
            }

            runStart = NextKept(classes, runEnd);
        }
    }

    /// <summary>Appends the positions and the types of the characters of the level run that starts at a kept character to the sequence.</summary>
    /// <returns>The end of the run: the removed characters do not end a run.</returns>
    private static int GatherRun(ReadOnlySpan<byte> classes, ReadOnlySpan<byte> levels, int start, Span<int> positions, Span<byte> sequenceTypes, ref int length)
    {
        var level = levels[start];
        var count = length;
        positions[count] = start;
        sequenceTypes[count] = classes[start];
        count++;
        var end = start + 1;
        for (var i = end; i < classes.Length; i++)
        {
            var value = classes[i];
            if (value == (byte)Bidi.BoundaryNeutral)
                continue;

            if (levels[i] != level)
                break;

            positions[count] = i;
            sequenceTypes[count] = value;
            count++;
            end = i + 1;
        }

        length = count;
        return end;
    }

    /// <summary>I1 and I2.</summary>
    private static int GetLevelIncrement(int level, Bidi value)
    {
        return (int)((((level & 1) == 0 ? EvenLevelIncrements : OddLevelIncrements) >> ((int)value * 2)) & 3);
    }

    private static int NextKept(ReadOnlySpan<byte> classes, int index)
    {
        while (index < classes.Length && classes[index] == (byte)Bidi.BoundaryNeutral)
        {
            index++;
        }

        return index;
    }

    private static int PreviousKept(ReadOnlySpan<byte> classes, int index)
    {
        index--;
        while (index >= 0 && classes[index] == (byte)Bidi.BoundaryNeutral)
        {
            index--;
        }

        return index;
    }

    /// <summary>Finds the end of the level run that starts at a kept character: the removed characters do not end a run.</summary>
    private static int FindRunEnd(ReadOnlySpan<byte> classes, ReadOnlySpan<byte> levels, int start)
    {
        var level = levels[start];
        var end = start + 1;
        for (var i = end; i < classes.Length; i++)
        {
            if (classes[i] == (byte)Bidi.BoundaryNeutral)
                continue;

            if (levels[i] != level)
                break;

            end = i + 1;
        }

        return end;
    }

    /// <summary>Resolves the types of an isolating run sequence: W1 to W7, N0, N1 and N2.</summary>
    /// <param name="text">The text, read for the paired brackets; empty when only the classes are known.</param>
    /// <param name="types">The types of the characters of the sequence, modified in place.</param>
    /// <param name="properties">The properties of the code units of the paragraph.</param>
    /// <param name="links">The links of the code units of the paragraph.</param>
    /// <param name="positions">The positions in the paragraph of the characters of the sequence.</param>
    /// <param name="level">The embedding level of the sequence.</param>
    /// <param name="startType">The type before the sequence (sos).</param>
    /// <param name="endType">The type after the sequence (eos).</param>
    /// <param name="classMask">A mask that contains every class of the sequence: the rules that cannot change anything are skipped.</param>
    private static void ResolveSequence(ReadOnlySpan<char> text, Span<byte> types, ReadOnlySpan<byte> properties, Span<int> links, ReadOnlySpan<int> positions, int level, Bidi startType, Bidi endType, int classMask)
    {
        if ((classMask & ((1 << (int)Bidi.NonspacingMark) | (1 << (int)Bidi.RightToLeftArabic))) != 0)
        {
            ResolveNonspacingMarksAndArabicLetters(types, startType);
        }

        if ((classMask & (SeparatorClasses | (1 << (int)Bidi.EuropeanTerminator) | (1 << (int)Bidi.EuropeanNumber))) != 0)
        {
            ResolveNumbers(types, startType);
        }

        var embedding = (level & 1) != 0 ? Bidi.RightToLeft : Bidi.LeftToRight;
        if (!text.IsEmpty && (classMask & HasBrackets) != 0)
        {
            ResolveBrackets(text, types, properties, links, positions, embedding, startType);
        }

        ResolveNeutralTypes(types, embedding, startType, endType);
    }

    /// <summary>N1 and N2: a sequence of neutrals takes the direction of the text on both sides when they agree, and the embedding direction otherwise.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ResolveNeutralTypes(Span<byte> types, Bidi embedding, Bidi startType, Bidi endType)
    {
        for (var k = 0; k < types.Length;)
        {
            if (!IsNeutral(types[k]))
            {
                k++;
                continue;
            }

            var start = k;
            do
            {
                k++;
            }
            while (k < types.Length && IsNeutral(types[k]));

            var leading = start == 0 ? startType : GetStrongDirection((Bidi)types[start - 1]);
            var trailing = k == types.Length ? endType : GetStrongDirection((Bidi)types[k]);
            var resolved = (byte)(leading == trailing ? leading : embedding);
            for (var j = start; j < k; j++)
            {
                types[j] = resolved;
            }
        }
    }

    /// <summary>W1 to W3: a nonspacing mark takes the type of the character before it; a European number after an Arabic letter is an Arabic number; then an Arabic letter is R.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ResolveNonspacingMarksAndArabicLetters(Span<byte> types, Bidi startType)
    {
        // W2 and W3 only depend on the types W1 gives to the characters before, so the three rules are one pass.
        var previous = startType;
        var lastStrong = startType;
        for (var k = 0; k < types.Length; k++)
        {
            var value = (Bidi)types[k];
            if (value is Bidi.NonspacingMark)
            {
                value = previous is Bidi.LeftToRightIsolate or Bidi.RightToLeftIsolate or Bidi.FirstStrongIsolate or Bidi.PopDirectionalIsolate
                    ? Bidi.OtherNeutral
                    : previous;
            }

            previous = value;
            switch (value)
            {
                case Bidi.LeftToRight or Bidi.RightToLeft:
                    lastStrong = value;
                    break;

                case Bidi.RightToLeftArabic:
                    lastStrong = Bidi.RightToLeftArabic;
                    value = Bidi.RightToLeft;
                    break;

                case Bidi.EuropeanNumber when lastStrong is Bidi.RightToLeftArabic:
                    value = Bidi.ArabicNumber;
                    break;
            }

            types[k] = (byte)value;
        }
    }

    /// <summary>
    /// W4 to W7: a single separator between two numbers of the same type takes their type; a sequence of European
    /// terminators next to a European number is part of the number; the other separators and terminators are
    /// neutral; a European number after L is L.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ResolveNumbers(Span<byte> types, Bidi startType)
    {
        // The rules are applied in one pass. W4 reads the type of the character before, as changed by W4 (previous),
        // and the type of the character after, which W4 has not changed yet. W5 reads the types W4 gives to the
        // characters around a sequence of terminators: W4 does not change the one after, which follows a terminator.
        // W7 reads the strong types, which W4 to W6 do not change.
        var lastStrong = startType;
        var previous = Bidi.OtherNeutral;
        for (var k = 0; k < types.Length;)
        {
            var value = (Bidi)types[k];
            if (value is Bidi.EuropeanTerminator)
            {
                var start = k;
                do
                {
                    k++;
                }
                while (k < types.Length && (Bidi)types[k] is Bidi.EuropeanTerminator);

                var isNumber = previous is Bidi.EuropeanNumber || (k < types.Length && (Bidi)types[k] is Bidi.EuropeanNumber);
                var resolved = (byte)(!isNumber ? Bidi.OtherNeutral : lastStrong is Bidi.LeftToRight ? Bidi.LeftToRight : Bidi.EuropeanNumber);
                for (var j = start; j < k; j++)
                {
                    types[j] = resolved;
                }

                previous = Bidi.EuropeanTerminator;
                continue;
            }

            if (value is Bidi.EuropeanSeparator or Bidi.CommonSeparator
                && k > 0 && k + 1 < types.Length && previous == (Bidi)types[k + 1]
                && (previous is Bidi.EuropeanNumber || (value is Bidi.CommonSeparator && previous is Bidi.ArabicNumber)))
            {
                value = previous;
            }

            previous = value;
            switch (value)
            {
                case Bidi.EuropeanSeparator or Bidi.CommonSeparator:
                    value = Bidi.OtherNeutral;
                    break;

                case Bidi.LeftToRight or Bidi.RightToLeft:
                    lastStrong = value;
                    break;

                case Bidi.EuropeanNumber when lastStrong is Bidi.LeftToRight:
                    value = Bidi.LeftToRight;
                    break;
            }

            types[k] = (byte)value;
            k++;
        }
    }

    private static bool IsNeutral(byte value)
    {
        return ((NeutralClasses >> value) & 1) != 0;
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

    private static UnicodeBidiPairedBracketType GetBracketType(byte properties)
    {
        return (UnicodeBidiPairedBracketType)((properties & BracketTypeBits) >> BracketTypeShift);
    }

    /// <summary>N0: gives a direction to the paired brackets of a sequence.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ResolveBrackets(ReadOnlySpan<char> text, Span<byte> types, ReadOnlySpan<byte> properties, Span<int> links, ReadOnlySpan<int> positions, Bidi embedding, Bidi startType)
    {
        if (!FindBracketPairs(text, types, properties, links, positions))
            return;

        // The pairs are visited in the order of their opening brackets. FindBracketPairs initialized the link of
        // every opening bracket whose type is ON, and N0 does not change the type of another opening bracket.
        for (var open = 0; open < types.Length; open++)
        {
            if ((Bidi)types[open] is not Bidi.OtherNeutral || GetBracketType(properties[positions[open]]) is not UnicodeBidiPairedBracketType.Open)
                continue;

            var link = links[positions[open]];
            if (link > -2)
                continue;

            var close = -(link + 2);
            links[positions[open]] = NoLink;

            var hasOpposite = false;
            var hasEmbedding = false;
            for (var k = open + 1; k < close; k++)
            {
                var direction = GetStrongDirection((Bidi)types[k]);
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
                    var direction = GetStrongDirection((Bidi)types[k]);
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

            SetBracket(types, properties, positions, open, resolved);
            SetBracket(types, properties, positions, close, resolved);
        }
    }

    private static void SetBracket(Span<byte> types, ReadOnlySpan<byte> properties, ReadOnlySpan<int> positions, int position, Bidi resolved)
    {
        types[position] = (byte)resolved;

        // The nonspacing marks that follow a bracket take its direction.
        for (var k = position + 1; k < types.Length; k++)
        {
            if ((Bidi)(properties[positions[k]] & ClassBits) is not Bidi.NonspacingMark)
                break;

            types[k] = (byte)resolved;
        }
    }

    /// <summary>BD16: stores in the link of each opening bracket of a pair the position in the sequence of its closing bracket, as -(position + 2).</summary>
    /// <returns><see langword="false"/> when the sequence has no pair.</returns>
    /// <remarks>
    /// The link of an opening bracket whose type is ON is set to <see cref="NoLink"/> when it is not paired, so the
    /// links do not need to be initialized: the link of a character that is ON is not used otherwise.
    /// </remarks>
    private static bool FindBracketPairs(ReadOnlySpan<char> text, ReadOnlySpan<byte> types, ReadOnlySpan<byte> properties, Span<int> links, ReadOnlySpan<int> positions)
    {
        Span<int> stackBrackets = stackalloc int[MaxBracketDepth];
        Span<int> stackPositions = stackalloc int[MaxBracketDepth];
        var depth = 0;
        var hasPair = false;
        for (var k = 0; k < types.Length; k++)
        {
            if ((Bidi)types[k] is not Bidi.OtherNeutral)
                continue;

            var index = positions[k];
            var type = GetBracketType(properties[index]);
            if (type is UnicodeBidiPairedBracketType.None)
                continue;

            if (type is UnicodeBidiPairedBracketType.Open)
            {
                links[index] = NoLink;
            }

            var codePoint = Unicode.DecodeAt(text, index, out _);
            if (!UnicodeProperties.TryGetBidiPairedBracket(codePoint, out var paired))
                continue;

            if (type is UnicodeBidiPairedBracketType.Open)
            {
                if (depth == MaxBracketDepth)
                {
                    // No pair at all for this sequence.
                    for (var j = 0; j < types.Length; j++)
                    {
                        if ((Bidi)types[j] is Bidi.OtherNeutral && GetBracketType(properties[positions[j]]) is UnicodeBidiPairedBracketType.Open)
                        {
                            links[positions[j]] = NoLink;
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

                    links[positions[stackPositions[entry]]] = -(k + 2);
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
