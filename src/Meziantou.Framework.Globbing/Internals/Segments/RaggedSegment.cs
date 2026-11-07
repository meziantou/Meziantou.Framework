using System.Buffers;
using System.Runtime.InteropServices;
using Meziantou.Framework.Globbing.Internals.Segments;

namespace Meziantou.Framework.Globbing.Internals;

internal sealed class RaggedSegment : Segment
{
    // The memo of the alternation matcher holds one bit per (instruction, position) pair. Up to this many 64-bit
    // words, it lives on the stack.
    private const int StackMemoWordCount = 64;

    internal readonly Segment[] _segments;
    private readonly int _minimumSegmentLength;
    private readonly char[]? _firstRequiredCharacters;
    private readonly Instruction[]? _program;
    private readonly bool _requiresLiteralLeadingDot;

    /// <param name="segments">The subsegments to match in order.</param>
    /// <param name="requiresLiteralLeadingDot">
    ///     Whether a path segment starting with a dot only matches when that dot is written in the pattern, rather
    ///     than matched by a wildcard. It is only needed when the segment starts with an alternation, as the
    ///     alternative that the path takes decides whether the dot is written.
    /// </param>
    public RaggedSegment(Segment[] segments, bool requiresLiteralLeadingDot)
    {
        _segments = segments;
        _requiresLiteralLeadingDot = requiresLiteralLeadingDot;
        _minimumSegmentLength = GetMinimumLength(segments);

        if (Array.Exists(segments, segment => segment is AlternationSegment))
        {
            var program = new List<Instruction>();
            Compile(program, segments);
            program.Add(new Instruction(InstructionKind.Match, segment: null, target: 0));
            _program = [.. program];
        }

        if (_segments.Length > 0)
        {
            _firstRequiredCharacters = GetLeadingCharacters(_segments);
        }
    }

    /// <summary>The characters the first character of the path segment must be one of, or <see langword="null"/> when the segment does not require any.</summary>
    internal char[]? FirstRequiredCharacters => _firstRequiredCharacters;

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.CurrentSegmentLength < _minimumSegmentLength)
            return false;

        if (_firstRequiredCharacters is not null)
        {
            if (pathReader.CurrentSegmentLength == 0)
                return false;

            if (Array.IndexOf(_firstRequiredCharacters, pathReader.CurrentText[0]) < 0)
                return false;
        }

        if (_program is null)
            return MatchWithoutAlternation(ref pathReader, _segments);

        var literalDotPending = _requiresLiteralLeadingDot && pathReader.CurrentSegmentLength > 0 && pathReader.CurrentText[0] == '.';
        return MatchProgram(ref pathReader, _program, literalDotPending);
    }

    /// <summary>
    ///     Matches subsegments that hold no alternation. Each subsegment other than '*' matches in at most one way, and
    ///     the text it consumes ends further when it starts further. So a '*' only has to be retried while the
    ///     subsegments that follow it, up to the next '*', do not match: once they do, matching them at their first
    ///     position leaves the most text to the rest of the pattern, and the earlier '*' never has to grow again. Only
    ///     the last '*' is remembered, which makes the cost O(n·m) instead of exponential in the number of '*'.
    /// </summary>
    private static bool MatchWithoutAlternation(ref PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
    {
        var current = pathReader;
        var index = 0;

        // The subsegment to restart from when the text that follows the last '*' does not match, and the position
        // of the text that this '*' consumes up to.
        var restartIndex = -1;
        var starReader = pathReader;
        while (true)
        {
            if (index == patternSegments.Length)
            {
                if (current.IsEndOfPath || current.IsPathSeparator())
                {
                    pathReader = current;
                    return true;
                }
            }
            else
            {
                var patternSegment = patternSegments[index];
                if (patternSegment is MatchAllSubSegment)
                {
                    restartIndex = index + 1;
                    starReader = current;
                    index++;
                    continue;
                }

                var copy = current;
                if (patternSegment.IsMatch(ref copy))
                {
                    current = copy;
                    index++;
                    continue;
                }
            }

            // Let the last '*' consume one more character, and match what follows it again
            if (restartIndex < 0 || starReader.IsEndOfCurrentSegment)
                return false;

            starReader.ConsumeInSegment(1);
            current = starReader;
            index = restartIndex;

            // A '*' is preceded by a prefilter that skips to the next character the following subsegment can start with
            if (restartIndex >= 2 && patternSegments[restartIndex - 2] is ConsumeSegmentUntilSegment skip)
            {
                if (!skip.IsMatch(ref current))
                    return false;

                starReader = current;
            }
        }
    }

    /// <summary>
    ///     Matches subsegments that hold an alternation, compiled into a program of <see cref="Instruction"/>. The
    ///     matcher backtracks, but it records every (instruction, position) pair that failed: the result of a pair
    ///     only depends on the pair itself, so no pair is evaluated twice and the cost is polynomial.
    /// </summary>
    /// <param name="literalDotPending">
    ///     Whether the path segment starts with a dot that only a literal can consume. It only applies at position 0,
    ///     which a (instruction, position) pair already identifies, so it does not have to be part of the memo.
    /// </param>
    private static bool MatchProgram(ref PathReader pathReader, Instruction[] program, bool literalDotPending)
    {
        var segmentLength = pathReader.CurrentSegmentLength;
        var bitCount = program.Length * (segmentLength + 1);
        var wordCount = (bitCount + 63) / 64;

        ulong[]? rented = null;
        var memo = wordCount <= StackMemoWordCount ? stackalloc ulong[StackMemoWordCount] : (rented = ArrayPool<ulong>.Shared.Rent(wordCount));
        memo = memo[..wordCount];
        memo.Clear();
        try
        {
            return Run(program, memo, segmentLength, literalDotPending, ref pathReader, 0);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<ulong>.Shared.Return(rented);
            }
        }

        static bool Run(Instruction[] program, scoped Span<ulong> memo, int segmentLength, bool literalDotPending, ref PathReader pathReader, int pc)
        {
            while (true)
            {
                var position = segmentLength - pathReader.CurrentSegmentLength;
                var bit = (pc * (segmentLength + 1)) + position;
                ref var word = ref memo[bit >> 6];
                var mask = 1UL << (bit & 63);

                // Instructions only jump forward without consuming, so a pair visited again already failed
                if ((word & mask) != 0)
                    return false;

                word |= mask;

                var dotPending = position == 0 && literalDotPending;
                var instruction = program[pc];
                switch (instruction.Kind)
                {
                    case InstructionKind.Match:
                        return pathReader.IsEndOfPath || pathReader.IsPathSeparator();

                    case InstructionKind.Jump:
                        pc = instruction.Target;
                        continue;

                    case InstructionKind.Split:
                    {
                        var copy = pathReader;
                        if (Run(program, memo, segmentLength, literalDotPending, ref copy, pc + 1))
                        {
                            pathReader = copy;
                            return true;
                        }

                        pc = instruction.Target;
                        continue;
                    }

                    case InstructionKind.Star:
                    {
                        // A wildcard never consumes the leading dot, even when it consumes nothing
                        if (dotPending)
                            return false;

                        var copy = pathReader;
                        if (Run(program, memo, segmentLength, literalDotPending, ref copy, pc + 1))
                        {
                            pathReader = copy;
                            return true;
                        }

                        if (pathReader.IsEndOfCurrentSegment)
                            return false;

                        pathReader.ConsumeInSegment(1);
                        continue;
                    }

                    default:
                    {
                        var segment = instruction.Segment!;
                        if (dotPending && segment is not LiteralSegment)
                            return false;

                        if (!segment.IsMatch(ref pathReader))
                            return false;

                        pc++;
                        continue;
                    }
                }
            }
        }
    }

    private static void Compile(List<Instruction> program, ReadOnlySpan<Segment> segments)
    {
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case MatchAllSubSegment:
                    program.Add(new Instruction(InstructionKind.Star, segment: null, target: 0));
                    break;

                case AlternationSegment alternation:
                {
                    // Split(next alternative) Alternative Jump(end) ... LastAlternative
                    var jumps = new List<int>(alternation.Alternatives.Length);
                    for (var i = 0; i < alternation.Alternatives.Length; i++)
                    {
                        var isLast = i == alternation.Alternatives.Length - 1;
                        var split = -1;
                        if (!isLast)
                        {
                            split = program.Count;
                            program.Add(default);
                        }

                        Compile(program, alternation.Alternatives[i]);

                        if (!isLast)
                        {
                            jumps.Add(program.Count);
                            program.Add(default);
                            program[split] = new Instruction(InstructionKind.Split, segment: null, target: program.Count);
                        }
                    }

                    foreach (var jump in jumps)
                    {
                        program[jump] = new Instruction(InstructionKind.Jump, segment: null, target: program.Count);
                    }

                    break;
                }

                default:
                    program.Add(new Instruction(InstructionKind.Segment, segment, target: 0));
                    break;
            }
        }
    }

    internal static int GetMinimumLength(ReadOnlySpan<Segment> segments)
    {
        var result = 0;
        foreach (var segment in segments)
        {
            result += GetMinimumLength(segment);
        }

        return result;
    }

    private static int GetMinimumLength(Segment segment)
    {
        return segment switch
        {
            LiteralSegment literal => literal.Value.Length,
            AlternationSegment alternation => GetMinimumAlternationLength(alternation.Alternatives),
            StartsWithSegment startsWith => startsWith.Value.Length,
            EndsWithSegment endsWith => endsWith.Value.Length,
            ContainsSegment contains => contains.Value.Length,

            MatchAnyCharacterSegment => 1,
            MatchAnyTextCharacterSegment => 1,
            CharacterSetSegment => 1,
            CharacterSetInverseSegment => 1,
            CharacterRangeSegment => 1,
            CharacterRangeInverseSegment => 1,
            CharacterRangeIgnoreCaseSegment => 1,
            CharacterRangeIgnoreCaseInverseSegment => 1,
            CharacterClassSegment => 1,
            OrSegment => 1,

            _ => 0,
        };

        static int GetMinimumAlternationLength(Segment[][] alternatives)
        {
            var minLength = int.MaxValue;
            foreach (var alternative in alternatives)
            {
                minLength = Math.Min(minLength, GetMinimumLength(alternative));
            }

            return minLength == int.MaxValue ? 0 : minLength;
        }
    }

    /// <summary>
    ///     Returns the characters that the text matched by <paramref name="segments"/> must start with, expanded for
    ///     the case when it is ignored, or <see langword="null"/> when they cannot be enumerated exactly.
    /// </summary>
    internal static char[]? GetLeadingCharacters(ReadOnlySpan<Segment> segments)
    {
        if (segments.IsEmpty)
            return null;

        switch (segments[0])
        {
            case LiteralSegment literal when literal.Value.Length > 0:
                return CreateCharacterSet([literal.Value[0]], literal.IgnoreCase);

            case CharacterSetSegment set:
                return CreateCharacterSet(set.Set.AsSpan(), set.IgnoreCase);

            case CharacterRangeSegment range when range.Range.Length <= 8:
                return range.Range.EnumerateCharacters();

            case AlternationSegment alternation:
            {
                var result = new HashSet<char>();
                foreach (var alternative in alternation.Alternatives)
                {
                    // An empty alternative consumes nothing, so the segment does not require any first character.
                    var characters = GetLeadingCharacters(alternative);
                    if (characters is null)
                        return null;

                    result.UnionWith(characters);
                }

                return result.Count == 0 ? null : [.. result];
            }
        }

        return null;

        static char[]? CreateCharacterSet(ReadOnlySpan<char> characters, bool ignoreCase)
        {
            return IgnoreCaseExpansion.TryExpand(characters, ignoreCase, out var result) ? result : null;
        }
    }

    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        foreach (var item in _segments)
        {
            item.AppendPattern(ref sb, dialect);
        }
    }

    public override string ToString() => ToString(GlobDialect.Standard);

    private enum InstructionKind
    {
        Segment,
        Star,
        Split,
        Jump,
        Match,
    }

    /// <summary>
    ///     An instruction of the alternation matcher. <see cref="InstructionKind.Split"/> tries the next instruction,
    ///     then <see cref="Target"/>. <see cref="InstructionKind.Star"/> tries the next instruction, then consumes
    ///     one character and tries again.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct Instruction
    {
        public Instruction(InstructionKind kind, Segment? segment, int target)
        {
            Kind = kind;
            Segment = segment;
            Target = target;
        }

        public InstructionKind Kind { get; }
        public Segment? Segment { get; }
        public int Target { get; }
    }
}
