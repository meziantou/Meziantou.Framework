using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Meziantou.Framework.WebPlatform.Unicode.BidiClass;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>How the embedding level of the paragraphs of a text is determined (see <see cref="BidiResolver"/>).</summary>
internal enum BidiParagraphDirection : byte
{
    /// <summary>Level 0, set by the higher-level protocol (clause HL1), e.g. the CSS <c>direction</c> of the block container.</summary>
    LeftToRight,

    /// <summary>Level 1, set by the higher-level protocol (clause HL1).</summary>
    RightToLeft,

    /// <summary>Rules P2 and P3; level 0 when the paragraph has no strong character.</summary>
    AutoLeftToRight,

    /// <summary>Rules P2 and P3; level 1 when the paragraph has no strong character (clause HL1).</summary>
    AutoRightToLeft,
}

/// <summary>A paragraph of the resolved text (rule P1): a range of UTF-16 code units, including the paragraph separator that ends it.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct BidiParagraph(int Start, int End, byte Level)
{
    public bool IsRightToLeft => (Level & 1) != 0;
}

/// <summary>A level run of a line (see <see cref="BidiResolver.GetVisualRuns"/>): a range of the line whose items are displayed in reverse order when the level is odd.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct BidiRun(int Start, int End, byte Level)
{
    public bool IsRightToLeft => (Level & 1) != 0;
}

/// <summary>
/// The Unicode Bidirectional Algorithm (https://www.unicode.org/reports/tr9/): resolves the embedding level of every character of a
/// text (rules P1 to I2), then of each line (rule L1), and reorders a line (rule L2). Rules L3 (combining marks) and L4 (mirroring)
/// belong to the rendering: <see cref="UnicodeProperties.GetBidiMirroringGlyph"/> gives the mirrored glyph of a character at an
/// odd level.
/// </summary>
/// <remarks>
/// <para>
/// The text is indexed by UTF-16 code unit: the low surrogate of a pair has the level of its high surrogate. The characters removed
/// by rule X9 (embedding and override initiators, PDF and BN) are retained as described in the implementation notes of UAX #9 (§5.2):
/// they get the level of the preceding character (the paragraph level at the start of a paragraph) and, in rule L1, they are reset
/// with the whitespace that precedes a separator or ends a line.
/// </para>
/// <para>
/// CSS (https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection) applies <c>unicode-bidi</c> by inserting the bidi
/// control codes of <see cref="BidiControls"/> in the text of the paragraph, and sets the paragraph level from the <c>direction</c>
/// of the block container (<see cref="BidiControls.GetParagraphDirection"/>).
/// </para>
/// <para>An instance reuses its buffers: it is not thread-safe.</para>
/// </remarks>
internal sealed class BidiResolver
{
    /// <summary>The maximum explicit embedding level (BD2).</summary>
    public const int MaxDepth = 125;

    // BD16: the bracket stack has a fixed size
    private const int MaxBracketStackDepth = 63;

    private BidiClass[] _originalClasses = []; // Bidi_Class of the code point of each code unit (rule L1)
    private BidiClass[] _initialTypes = [];    // Bidi_Class of each code unit; BN for the low surrogate of a pair
    private BidiClass[] _types = [];           // the types modified by the rules
    private byte[] _levels = [];
    private int[] _isolateMatches = [];        // BD9: the matching PDI of an isolate initiator, the initiator of a PDI, or -1
    private char[] _text = [];                 // to identify the paired brackets
    private bool _hasText;
    private int _length;
    private readonly List<BidiParagraph> _paragraphs = [];

    // Buffers of the resolution of a paragraph
    private int[] _indexes = [];               // the characters that are not removed by X9
    private int[] _positions = [];             // the position of each character in _indexes
    private int[] _levelRuns = [];             // the start of each level run in _indexes (followed by the count of _indexes)
    private int[] _runOfPosition = [];         // the level run that starts at each position of _indexes
    private int[] _sequence = [];              // the characters of the current isolating run sequence
    private BidiClass[] _sequenceTypes = [];   // the types of the sequence before W1
    private int[] _stack = [];                 // matching of the isolates (BD9), then of the brackets (BD16)
    private readonly List<(int Opening, int Closing)> _bracketPairs = [];

    /// <summary>The number of resolved UTF-16 code units.</summary>
    public int Length => _length;

    /// <summary>The embedding level of each code unit of the text (rules P1 to I2, before L1).</summary>
    public ReadOnlySpan<byte> Levels => _levels.AsSpan(0, _length);

    /// <summary>The paragraphs of the text (rule P1).</summary>
    public IReadOnlyList<BidiParagraph> Paragraphs => _paragraphs;

    /// <summary>Returns the embedding levels of the code units of a single-line text (rules P1 to L1).</summary>
    public static byte[] GetLevels(ReadOnlySpan<char> text, BidiParagraphDirection direction)
    {
        var resolver = new BidiResolver();
        resolver.Resolve(text, direction);
        var levels = new byte[text.Length];
        resolver.GetLineLevels(0, text.Length, levels);
        return levels;
    }

    /// <summary>Returns the Bidi_Class of each code unit of the text (the low surrogate of a pair has the class of the code point).</summary>
    public static void GetClasses(ReadOnlySpan<char> text, Span<BidiClass> classes)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                classes[i] = classes[i + 1] = UnicodeProperties.GetBidiClass(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
                continue;
            }

            classes[i] = UnicodeProperties.GetBidiClass(text[i]);
        }
    }

    /// <summary>Resolves the embedding levels of a text (rules P1 to I2) from the Bidi_Class of its characters.</summary>
    public void Resolve(ReadOnlySpan<char> text, BidiParagraphDirection direction)
    {
        EnsureCapacity(text.Length);
        GetClasses(text, _originalClasses);
        ResolveCore(text, text.Length, direction);
    }

    /// <summary>
    /// Resolves the embedding levels of a text (rules P1 to I2) whose Bidi_Class are given: for instance, the layout gives a strong
    /// class to an atomic inline (https://drafts.csswg.org/css-writing-modes-4/#bidi-atomic-inline).
    /// </summary>
    /// <param name="text">The text, used to identify the paired brackets (rule N0) and the surrogate pairs, or empty to use the classes only (as in <c>BidiTest.txt</c>).</param>
    /// <param name="classes">The class of each code unit (the low surrogate of a pair has the class of the code point).</param>
    /// <param name="direction">How the embedding level of each paragraph is determined.</param>
    public void Resolve(ReadOnlySpan<char> text, ReadOnlySpan<BidiClass> classes, BidiParagraphDirection direction)
    {
        Debug.Assert(text.IsEmpty || text.Length == classes.Length);
        EnsureCapacity(classes.Length);
        classes.CopyTo(_originalClasses);
        ResolveCore(text, classes.Length, direction);
    }

    private void ResolveCore(ReadOnlySpan<char> text, int length, BidiParagraphDirection direction)
    {
        _length = length;
        _hasText = !text.IsEmpty;
        _paragraphs.Clear();
        _originalClasses.AsSpan(0, length).CopyTo(_initialTypes);
        if (_hasText)
        {
            text.CopyTo(_text);

            // The low surrogate of a pair is ignored like a BN, so it gets the level of its high surrogate
            for (var i = 1; i < length; i++)
            {
                if (char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1]))
                {
                    _initialTypes[i] = BN;
                }
            }
        }

        // P1: split the text into paragraphs; a paragraph separator is kept with the preceding paragraph
        var start = 0;
        for (var i = 0; i < length; i++)
        {
            if (_initialTypes[i] == B)
            {
                ResolveParagraph(start, i + 1, direction);
                start = i + 1;
            }
        }

        if (start < length || length == 0)
        {
            ResolveParagraph(start, length, direction);
        }
    }

    /// <summary>Returns the index of the paragraph that contains the code unit at <paramref name="index"/>.</summary>
    public int GetParagraphIndex(int index)
    {
        var low = 0;
        var high = _paragraphs.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) >> 1;
            if (_paragraphs[middle].Start <= index)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>
    /// Writes the levels of the line <c>[start, end)</c> of the resolved text to <paramref name="lineLevels"/>, after rule L1
    /// (https://www.unicode.org/reports/tr9/#L1): the segment separators, the paragraph separators, and the sequences of whitespace,
    /// isolate formatting characters and characters removed by X9 that precede them or end the line get the paragraph level.
    /// The last <paramref name="preservedTrailingLength"/> characters of the line are not a part of the whitespace that ends
    /// it (the spaces that take space at the end of a line of <c>white-space: break-spaces</c>): they keep their level.
    /// </summary>
    public void GetLineLevels(int start, int end, Span<byte> lineLevels, int preservedTrailingLength = 0)
    {
        Debug.Assert(start >= 0 && start <= end && end <= _length && lineLevels.Length >= end - start);
        if (start == end)
            return;

        _levels.AsSpan(start, end - start).CopyTo(lineLevels);
        var paragraph = GetParagraphIndex(end - 1);
        var trailing = preservedTrailingLength == 0;
        for (var i = end - 1 - preservedTrailingLength; i >= start; i--)
        {
            while (_paragraphs[paragraph].Start > i)
            {
                paragraph--;
            }

            // The original types (a low surrogate has the class of its code point, so a pair is reset as a whole)
            var type = _originalClasses[i];
            if (type is B or S)
            {
                lineLevels[i - start] = _paragraphs[paragraph].Level;
                trailing = true;
            }
            else if (trailing && type is WS or FSI or LRI or RLI or PDI or BN or LRE or RLE or LRO or RLO or PDF)
            {
                lineLevels[i - start] = _paragraphs[paragraph].Level;
            }
            else
            {
                trailing = false;
            }
        }
    }

    /// <summary>
    /// Rule L2 (https://www.unicode.org/reports/tr9/#L2): computes the visual order of the items of a line from their levels.
    /// <c>visualToLogical[i]</c> is the logical index of the item displayed at the visual position <c>i</c> (from the line-left
    /// edge). The items can be the characters (with the levels of <see cref="GetLineLevels"/>) or the fragments of the line.
    /// </summary>
    public static void ReorderVisual(ReadOnlySpan<byte> levels, Span<int> visualToLogical)
    {
        Debug.Assert(visualToLogical.Length >= levels.Length);
        var highest = 0;
        var lowestOdd = MaxDepth + 2;
        for (var i = 0; i < levels.Length; i++)
        {
            visualToLogical[i] = i;
            var level = levels[i];
            highest = Math.Max(highest, level);
            if ((level & 1) != 0)
            {
                lowestOdd = Math.Min(lowestOdd, level);
            }
        }

        // From the highest level to the lowest odd level, reverse any contiguous sequence of items at that level or higher
        for (var level = highest; level >= lowestOdd; level--)
        {
            var i = 0;
            while (i < levels.Length)
            {
                if (levels[visualToLogical[i]] < level)
                {
                    i++;
                    continue;
                }

                var runStart = i;
                while (i < levels.Length && levels[visualToLogical[i]] >= level)
                {
                    i++;
                }

                visualToLogical[runStart..i].Reverse();
            }
        }
    }

    /// <summary>
    /// Splits a line into its level runs (the maximal ranges of items of the same level) and appends them to <paramref name="runs"/>
    /// in visual order, from the line-left edge (rule L2): the items of a run are displayed in reverse order when its level is odd.
    /// </summary>
    public static void GetVisualRuns(ReadOnlySpan<byte> lineLevels, List<BidiRun> runs)
    {
        var first = runs.Count;
        var highest = 0;
        var lowestOdd = MaxDepth + 2;
        for (var i = 0; i < lineLevels.Length;)
        {
            var start = i;
            var level = lineLevels[i];
            while (i < lineLevels.Length && lineLevels[i] == level)
            {
                i++;
            }

            runs.Add(new BidiRun(start, i, level));
            highest = Math.Max(highest, level);
            if ((level & 1) != 0)
            {
                lowestOdd = Math.Min(lowestOdd, level);
            }
        }

        for (var level = highest; level >= lowestOdd; level--)
        {
            var i = first;
            while (i < runs.Count)
            {
                if (runs[i].Level < level)
                {
                    i++;
                    continue;
                }

                var runStart = i;
                while (i < runs.Count && runs[i].Level >= level)
                {
                    i++;
                }

                runs.Reverse(runStart, i - runStart);
            }
        }
    }

    private void EnsureCapacity(int length)
    {
        if (_initialTypes.Length >= length)
            return;

        var capacity = Math.Max(length, _initialTypes.Length * 2);
        _originalClasses = new BidiClass[capacity];
        _initialTypes = new BidiClass[capacity];
        _types = new BidiClass[capacity];
        _levels = new byte[capacity];
        _isolateMatches = new int[capacity];
        _text = new char[capacity];
        _indexes = new int[capacity];
        _positions = new int[capacity];
        _levelRuns = new int[capacity + 1];
        _runOfPosition = new int[capacity];
        _sequence = new int[capacity];
        _sequenceTypes = new BidiClass[capacity];
        _stack = new int[Math.Max(capacity, MaxBracketStackDepth * 2)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIsolateInitiator(BidiClass type) => type is LRI or RLI or FSI;

    // X9: the characters removed from the implicit processing
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRemovedByX9(BidiClass initialType) => initialType is RLE or LRE or RLO or LRO or PDF or BN;

    // NI: the neutral and isolate formatting characters (BD12)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNeutralOrIsolate(BidiClass type) => type is B or S or WS or ON or LRI or RLI or FSI or PDI;

    // The strong direction for N0 and N1, where EN and AN are treated as R; ON for the other types
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BidiClass GetStrongDirection(BidiClass type) => type switch
    {
        L => L,
        R or EN or AN => R,
        _ => ON,
    };

    private void ResolveParagraph(int start, int end, BidiParagraphDirection direction)
    {
        MatchIsolates(start, end);

        // P2, P3 (or HL1)
        byte paragraphLevel;
        if (direction is BidiParagraphDirection.LeftToRight or BidiParagraphDirection.RightToLeft)
        {
            paragraphLevel = direction == BidiParagraphDirection.RightToLeft ? (byte)1 : (byte)0;
        }
        else
        {
            var level = FindFirstStrongLevel(start, end);
            paragraphLevel = (byte)(level >= 0 ? level : direction == BidiParagraphDirection.AutoRightToLeft ? 1 : 0);
        }

        _paragraphs.Add(new BidiParagraph(start, end, paragraphLevel));
        if (start == end)
            return;

        ResolveExplicitLevels(start, end, paragraphLevel);

        // X10: the level runs (BD7) of the characters that are not removed by X9
        var count = 0;
        var runCount = 0;
        for (var i = start; i < end; i++)
        {
            if (IsRemovedByX9(_initialTypes[i]))
                continue;

            if (count == 0 || _levels[i] != _levels[_indexes[count - 1]])
            {
                _runOfPosition[count] = runCount;
                _levelRuns[runCount++] = count;
            }

            _positions[i] = count;
            _indexes[count++] = i;
        }

        _levelRuns[runCount] = count;

        // BD13: the isolating run sequences
        for (var run = 0; run < runCount; run++)
        {
            var first = _indexes[_levelRuns[run]];
            if (_initialTypes[first] == PDI && _isolateMatches[first] >= 0)
                continue; // part of the sequence of its isolate initiator

            var length = 0;
            var current = run;
            while (true)
            {
                for (var position = _levelRuns[current]; position < _levelRuns[current + 1]; position++)
                {
                    _sequence[length++] = _indexes[position];
                }

                // The level run that starts with the matching PDI of the isolate initiator that ends the run, if any (the
                // characters between them have higher levels or are removed, so the PDI starts a level run)
                var last = _sequence[length - 1];
                if (!IsIsolateInitiator(_initialTypes[last]) || _isolateMatches[last] < 0)
                    break;

                current = _runOfPosition[_positions[_isolateMatches[last]]];
            }

            // sos and eos: the higher of the level of the sequence and of the adjacent characters (not removed), or of the paragraph
            var level = _levels[_sequence[0]];
            var firstPosition = _levelRuns[run];
            var previousLevel = firstPosition > 0 ? _levels[_indexes[firstPosition - 1]] : paragraphLevel;
            var lastCharacter = _sequence[length - 1];
            var lastPosition = _positions[lastCharacter];
            var nextLevel = IsIsolateInitiator(_initialTypes[lastCharacter]) || lastPosition + 1 >= count ? paragraphLevel : _levels[_indexes[lastPosition + 1]];
            var sos = (Math.Max(previousLevel, level) & 1) != 0 ? R : L;
            var eos = (Math.Max(nextLevel, level) & 1) != 0 ? R : L;
            ResolveSequence(_sequence.AsSpan(0, length), level, sos, eos);
        }

        // I1, I2 (after all the sequences: sos and eos use the explicit levels)
        for (var position = 0; position < count; position++)
        {
            var i = _indexes[position];
            var type = _types[i];
            if ((_levels[i] & 1) == 0)
            {
                if (type == R)
                {
                    _levels[i] += 1;
                }
                else if (type is AN or EN)
                {
                    _levels[i] += 2;
                }
            }
            else if (type is L or EN or AN)
            {
                _levels[i] += 1;
            }
        }

        // The characters removed by X9 get the level of the preceding character (UAX #9 §5.2)
        for (var i = start; i < end; i++)
        {
            if (IsRemovedByX9(_initialTypes[i]))
            {
                _levels[i] = i > start ? _levels[i - 1] : paragraphLevel;
            }
        }
    }

    // BD9: the matching PDI of each isolate initiator of a paragraph
    private void MatchIsolates(int start, int end)
    {
        var depth = 0;
        for (var i = start; i < end; i++)
        {
            var type = _initialTypes[i];
            if (IsIsolateInitiator(type))
            {
                _isolateMatches[i] = -1;
                _stack[depth++] = i;
            }
            else if (type == PDI)
            {
                _isolateMatches[i] = -1;
                if (depth > 0)
                {
                    var initiator = _stack[--depth];
                    _isolateMatches[initiator] = i;
                    _isolateMatches[i] = initiator;
                }
            }
        }
    }

    // P2, P3: the level given by the first strong character, skipping the isolates; -1 when there is none
    private int FindFirstStrongLevel(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var type = _initialTypes[i];
            if (type == L)
                return 0;

            if (type is R or AL)
                return 1;

            if (IsIsolateInitiator(type))
            {
                if (_isolateMatches[i] < 0 || _isolateMatches[i] >= end)
                    return -1;

                i = _isolateMatches[i];
            }
        }

        return -1;
    }

    // X1 to X8 (https://www.unicode.org/reports/tr9/#Explicit_Levels_and_Directions)
    private void ResolveExplicitLevels(int start, int end, byte paragraphLevel)
    {
        // The directional status stack (ON: neutral directional override status)
        Span<byte> stackLevels = stackalloc byte[MaxDepth + 2];
        Span<BidiClass> stackOverrides = stackalloc BidiClass[MaxDepth + 2];
        Span<bool> stackIsolates = stackalloc bool[MaxDepth + 2];
        stackLevels[0] = paragraphLevel;
        stackOverrides[0] = ON;
        stackIsolates[0] = false;
        var depth = 1;
        var overflowIsolateCount = 0;
        var overflowEmbeddingCount = 0;
        var validIsolateCount = 0;

        for (var i = start; i < end; i++)
        {
            var type = _initialTypes[i];
            _types[i] = type;
            var top = depth - 1;
            switch (type)
            {
                case RLE or LRE or RLO or LRO:
                {
                    // X2 to X5
                    _levels[i] = stackLevels[top];
                    var newLevel = type is RLE or RLO ? (stackLevels[top] + 1) | 1 : (stackLevels[top] + 2) & ~1;
                    if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                    {
                        stackLevels[depth] = (byte)newLevel;
                        stackOverrides[depth] = type switch { RLO => R, LRO => L, _ => ON };
                        stackIsolates[depth] = false;
                        depth++;
                    }
                    else if (overflowIsolateCount == 0)
                    {
                        overflowEmbeddingCount++;
                    }

                    _types[i] = BN; // X9
                    break;
                }

                case RLI or LRI or FSI:
                {
                    // X5a to X5c
                    _levels[i] = stackLevels[top];
                    if (stackOverrides[top] != ON)
                    {
                        _types[i] = stackOverrides[top];
                    }

                    var isRightToLeft = type == RLI || (type == FSI && FindFirstStrongLevel(i + 1, _isolateMatches[i] >= 0 ? _isolateMatches[i] : end) == 1);
                    var newLevel = isRightToLeft ? (stackLevels[top] + 1) | 1 : (stackLevels[top] + 2) & ~1;
                    if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                    {
                        validIsolateCount++;
                        stackLevels[depth] = (byte)newLevel;
                        stackOverrides[depth] = ON;
                        stackIsolates[depth] = true;
                        depth++;
                    }
                    else
                    {
                        overflowIsolateCount++;
                    }

                    break;
                }

                case PDI:
                {
                    // X6a
                    if (overflowIsolateCount > 0)
                    {
                        overflowIsolateCount--;
                    }
                    else if (validIsolateCount > 0)
                    {
                        overflowEmbeddingCount = 0;
                        while (!stackIsolates[depth - 1])
                        {
                            depth--;
                        }

                        depth--;
                        validIsolateCount--;
                    }

                    top = depth - 1;
                    _levels[i] = stackLevels[top];
                    if (stackOverrides[top] != ON)
                    {
                        _types[i] = stackOverrides[top];
                    }

                    break;
                }

                case PDF:
                {
                    // X7
                    if (overflowIsolateCount == 0)
                    {
                        if (overflowEmbeddingCount > 0)
                        {
                            overflowEmbeddingCount--;
                        }
                        else if (!stackIsolates[top] && depth >= 2)
                        {
                            depth--;
                        }
                    }

                    _levels[i] = stackLevels[depth - 1];
                    _types[i] = BN; // X9
                    break;
                }

                case B:
                    // X8
                    _levels[i] = paragraphLevel;
                    break;

                case BN:
                    _levels[i] = stackLevels[top];
                    break;

                default:
                    // X6
                    _levels[i] = stackLevels[top];
                    if (stackOverrides[top] != ON)
                    {
                        _types[i] = stackOverrides[top];
                    }

                    break;
            }
        }
    }

    // W1 to W7, N0 to N2 on an isolating run sequence (https://www.unicode.org/reports/tr9/#Resolving_Weak_Types)
    private void ResolveSequence(Span<int> sequence, byte level, BidiClass sos, BidiClass eos)
    {
        var types = _types;
        var length = sequence.Length;
        for (var k = 0; k < length; k++)
        {
            _sequenceTypes[k] = types[sequence[k]];
        }

        // W1
        for (var k = 0; k < length; k++)
        {
            ref var type = ref types[sequence[k]];
            if (type == NSM)
            {
                if (k == 0)
                {
                    type = sos;
                }
                else
                {
                    var previous = types[sequence[k - 1]];
                    type = previous is LRI or RLI or FSI or PDI ? ON : previous;
                }
            }
        }

        // W2, W3
        var lastStrong = sos;
        for (var k = 0; k < length; k++)
        {
            ref var type = ref types[sequence[k]];
            if (type is L or R or AL)
            {
                lastStrong = type;
                if (type == AL)
                {
                    type = R;
                }
            }
            else if (type == EN && lastStrong == AL)
            {
                type = AN;
            }
        }

        // W4
        for (var k = 1; k < length - 1; k++)
        {
            ref var type = ref types[sequence[k]];
            if (type is ES or CS)
            {
                var previous = types[sequence[k - 1]];
                var next = types[sequence[k + 1]];
                if (previous == EN && next == EN)
                {
                    type = EN;
                }
                else if (type == CS && previous == AN && next == AN)
                {
                    type = AN;
                }
            }
        }

        // W5
        for (var k = 0; k < length; k++)
        {
            if (types[sequence[k]] != ET)
                continue;

            var runStart = k;
            while (k < length && types[sequence[k]] == ET)
            {
                k++;
            }

            if ((runStart > 0 && types[sequence[runStart - 1]] == EN) || (k < length && types[sequence[k]] == EN))
            {
                for (var m = runStart; m < k; m++)
                {
                    types[sequence[m]] = EN;
                }
            }
        }

        // W6, W7
        lastStrong = sos;
        for (var k = 0; k < length; k++)
        {
            ref var type = ref types[sequence[k]];
            if (type is ES or ET or CS)
            {
                type = ON;
            }
            else if (type is L or R)
            {
                lastStrong = type;
            }
            else if (type == EN && lastStrong == L)
            {
                type = L;
            }
        }

        var embeddingDirection = (level & 1) != 0 ? R : L;
        if (_hasText)
        {
            ResolvePairedBrackets(sequence, embeddingDirection, sos);
        }

        // N1, N2
        for (var k = 0; k < length; k++)
        {
            if (!IsNeutralOrIsolate(types[sequence[k]]))
                continue;

            var runStart = k;
            while (k < length && IsNeutralOrIsolate(types[sequence[k]]))
            {
                k++;
            }

            var leading = runStart == 0 ? sos : GetStrongDirection(types[sequence[runStart - 1]]);
            var trailing = k == length ? eos : GetStrongDirection(types[sequence[k]]);
            var resolved = leading == trailing ? leading : embeddingDirection;
            for (var m = runStart; m < k; m++)
            {
                types[sequence[m]] = resolved;
            }
        }
    }

    // N0 (https://www.unicode.org/reports/tr9/#N0)
    private void ResolvePairedBrackets(Span<int> sequence, BidiClass embeddingDirection, BidiClass sos)
    {
        // BD16: identify the bracket pairs; a stack entry is the Bidi_Paired_Bracket of an opening bracket and its position
        var types = _types;
        var pairs = _bracketPairs;
        pairs.Clear();
        var stack = _stack;
        var depth = 0;
        for (var k = 0; k < sequence.Length; k++)
        {
            var index = sequence[k];
            if (types[index] != ON)
                continue;

            int codePoint = _text[index];
            var bracketType = UnicodeProperties.GetBidiPairedBracketType(codePoint);
            if (bracketType == BidiPairedBracketType.Open)
            {
                if (depth == MaxBracketStackDepth)
                    return; // stop processing BD16 and return an empty list

                stack[depth * 2] = NormalizeBracket(UnicodeProperties.GetBidiPairedBracket(codePoint));
                stack[(depth * 2) + 1] = k;
                depth++;
            }
            else if (bracketType == BidiPairedBracketType.Close)
            {
                var closing = NormalizeBracket(codePoint);
                for (var entry = depth - 1; entry >= 0; entry--)
                {
                    if (stack[entry * 2] == closing)
                    {
                        pairs.Add((stack[(entry * 2) + 1], k));
                        depth = entry;
                        break;
                    }
                }
            }
        }

        if (pairs.Count == 0)
            return;

        pairs.Sort(static (a, b) => a.Opening.CompareTo(b.Opening));
        foreach (var (opening, closing) in pairs)
        {
            // The strong types enclosed by the pair (EN and AN are R)
            var found = ON;
            for (var k = opening + 1; k < closing; k++)
            {
                var direction = GetStrongDirection(types[sequence[k]]);
                if (direction == ON)
                    continue;

                found = direction;
                if (direction == embeddingDirection)
                    break;
            }

            if (found == ON)
                continue; // N0 d: resolved by N1 and N2

            var resolved = embeddingDirection; // N0 b
            if (found != embeddingDirection)
            {
                // N0 c: the preceding strong type (EN and AN are R), or sos
                var context = sos;
                for (var k = opening - 1; k >= 0; k--)
                {
                    var direction = GetStrongDirection(types[sequence[k]]);
                    if (direction != ON)
                    {
                        context = direction;
                        break;
                    }
                }

                if (context == found)
                {
                    resolved = found; // N0 c 1; otherwise N0 c 2
                }
            }

            SetBracketType(sequence, opening, resolved);
            SetBracketType(sequence, closing, resolved);
        }

        pairs.Clear();
    }

    // Sets the type of a bracket resolved by N0, and of the characters that follow it and were NSM before W1
    private void SetBracketType(Span<int> sequence, int position, BidiClass type)
    {
        _types[sequence[position]] = type;
        for (var k = position + 1; k < sequence.Length && _sequenceTypes[k] == NSM; k++)
        {
            _types[sequence[k]] = type;
        }
    }

    // BD16: U+2329 and U+232A are canonically equivalent to U+3008 and U+3009
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NormalizeBracket(int codePoint) => codePoint switch
    {
        0x2329 => 0x3008,
        0x232A => 0x3009,
        _ => codePoint,
    };
}
