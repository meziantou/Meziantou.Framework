namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The intermediate representation of a pattern, between the syntax tree and the program: each node is one of the
/// matchers of the pattern semantics (https://tc39.es/ecma262/#sec-pattern-semantics), with the flags that apply to it
/// (the modifiers groups change them for a subpattern).
/// </summary>
internal abstract class RegExpNode
{
    private int _minLength = -1;

    /// <summary>Gets the smallest number of code units the node consumes.</summary>
    public int MinLength => _minLength >= 0 ? _minLength : _minLength = ComputeMinLength();

    protected abstract int ComputeMinLength();
}

/// <summary>EmptyMatcher</summary>
internal sealed class RegExpEmptyNode : RegExpNode
{
    public static RegExpEmptyNode Instance { get; } = new();

    protected override int ComputeMinLength() => 0;
}

/// <summary>MatchSequence: the items in order (reversed when matching backward).</summary>
internal sealed class RegExpSequenceNode(RegExpNode[] items) : RegExpNode
{
    public RegExpNode[] Items { get; } = items;

    protected override int ComputeMinLength()
    {
        var length = 0L;
        foreach (var item in Items)
        {
            length += item.MinLength;
        }

        return (int)Math.Min(length, int.MaxValue);
    }
}

/// <summary>MatchTwoAlternatives, for each alternative in order.</summary>
internal sealed class RegExpAlternationNode(RegExpNode[] alternatives) : RegExpNode
{
    public RegExpNode[] Alternatives { get; } = alternatives;

    protected override int ComputeMinLength()
    {
        var length = int.MaxValue;
        foreach (var alternative in Alternatives)
        {
            length = Math.Min(length, alternative.MinLength);
        }

        return length;
    }
}

/// <summary>A CharacterSetMatcher of one character: a code unit, or a code point when the pattern has the "u" or "v" flag.</summary>
internal sealed class RegExpCharNode(int value, bool ignoreCase) : RegExpNode
{
    public int Value { get; } = value;

    public bool IgnoreCase { get; } = ignoreCase;

    protected override int ComputeMinLength() => Value > 0xFFFF ? 2 : 1;
}

/// <summary>A CharacterSetMatcher. The set is the one of the specification: the characters that ignore case are added by the code generator.</summary>
internal sealed class RegExpSetNode(CodePointSet set, bool invert, bool ignoreCase) : RegExpNode
{
    public CodePointSet Set { get; } = set;

    public bool Invert { get; } = invert;

    public bool IgnoreCase { get; } = ignoreCase;

    protected override int ComputeMinLength() => 1;
}

/// <summary>The <c>.</c> atom: any character but the line terminators, unless dotAll.</summary>
internal sealed class RegExpDotNode(bool dotAll) : RegExpNode
{
    public bool DotAll { get; } = dotAll;

    protected override int ComputeMinLength() => 1;
}

internal enum RegExpAssertionKind
{
    Start,
    End,
    WordBoundary,
    NotWordBoundary,
}

/// <summary><c>^</c>, <c>$</c>, <c>\b</c>, <c>\B</c></summary>
internal sealed class RegExpAssertionNode(RegExpAssertionKind kind, bool multiline, bool ignoreCase) : RegExpNode
{
    public RegExpAssertionKind Kind { get; } = kind;

    public bool Multiline { get; } = multiline;

    public bool IgnoreCase { get; } = ignoreCase;

    protected override int ComputeMinLength() => 0;
}

/// <summary>A capturing group.</summary>
internal sealed class RegExpGroupNode(int index, RegExpNode body) : RegExpNode
{
    public int Index { get; } = index;

    public RegExpNode Body { get; } = body;

    protected override int ComputeMinLength() => Body.MinLength;
}

/// <summary>A lookahead or a lookbehind. <see cref="FirstGroup"/> and <see cref="GroupCount"/> are the groups it contains.</summary>
internal sealed class RegExpLookaroundNode(bool behind, bool negative, RegExpNode body, int firstGroup, int groupCount) : RegExpNode
{
    public bool Behind { get; } = behind;

    public bool Negative { get; } = negative;

    public RegExpNode Body { get; } = body;

    public int FirstGroup { get; } = firstGroup;

    public int GroupCount { get; } = groupCount;

    protected override int ComputeMinLength() => 0;
}

/// <summary>BackreferenceMatcher; a named backreference lists every group of that name.</summary>
internal sealed class RegExpBackreferenceNode(int[] groups, bool ignoreCase) : RegExpNode
{
    public int[] Groups { get; } = groups;

    public bool IgnoreCase { get; } = ignoreCase;

    protected override int ComputeMinLength() => 0;
}

/// <summary>RepeatMatcher. <see cref="FirstGroup"/> and <see cref="GroupCount"/> are the groups the atom contains (parenIndex + 1, parenCount).</summary>
internal sealed class RegExpQuantifierNode(RegExpNode body, int min, int max, bool greedy, int firstGroup, int groupCount) : RegExpNode
{
    /// <summary>The value of <see cref="Max"/> for an unbounded quantifier.</summary>
    public const int Infinite = int.MaxValue;

    public RegExpNode Body { get; } = body;

    public int Min { get; } = min;

    public int Max { get; } = max;

    public bool Greedy { get; } = greedy;

    public int FirstGroup { get; } = firstGroup;

    public int GroupCount { get; } = groupCount;

    protected override int ComputeMinLength() => (int)Math.Min((long)Body.MinLength * Min, int.MaxValue);
}
