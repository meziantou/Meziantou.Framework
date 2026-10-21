using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Generates the program of a pattern from its <see cref="RegExpNode"/> tree.</summary>
internal sealed class RegExpCodeGenerator
{
    private readonly List<int> _code = [];
    private readonly List<RegExpCharClass> _classes = [];
    private readonly List<string> _strings = [];
    private readonly List<RegExpLookaroundInfo> _lookarounds = [];
    private readonly List<RegExpBackreferenceInfo> _backreferences = [];
    private readonly bool _unicode;
    private int _registerCount;

    private RegExpCodeGenerator(bool unicode)
    {
        _unicode = unicode;
    }

    public static RegExpProgram Generate(RegExpParseResult parsed, RegExpFlags flags, string source)
    {
        var unicode = flags.HasEitherUnicodeFlag();
        var generator = new RegExpCodeGenerator(unicode);
        generator.Emit(parsed.Root, backward: false);
        generator._code.Add(RegExpOpCode.Match);

        var search = RegExpSearchInfo.Create(parsed.Root, unicode);
        return new RegExpProgram(
            source,
            flags,
            [.. generator._code],
            [.. generator._classes],
            [.. generator._strings],
            [.. generator._lookarounds],
            [.. generator._backreferences],
            generator._registerCount,
            parsed.GroupCount,
            parsed.GroupNames,
            search);
    }

    private int Position => _code.Count;

    private void Emit(RegExpNode node, bool backward)
    {
        switch (node)
        {
            case RegExpEmptyNode:
                break;

            case RegExpSequenceNode sequence:
                EmitSequence(sequence, backward);
                break;

            case RegExpAlternationNode alternation:
                EmitAlternation(alternation, backward);
                break;

            case RegExpCharNode or RegExpSetNode or RegExpDotNode:
                EmitSingleCharacter(node, backward);
                break;

            case RegExpAssertionNode assertion:
                _code.Add(assertion.Kind switch
                {
                    RegExpAssertionKind.Start => assertion.Multiline ? RegExpOpCode.AssertStartLine : RegExpOpCode.AssertStart,
                    RegExpAssertionKind.End => assertion.Multiline ? RegExpOpCode.AssertEndLine : RegExpOpCode.AssertEnd,
                    RegExpAssertionKind.WordBoundary => _unicode && assertion.IgnoreCase ? RegExpOpCode.WordBoundaryIgnoreCase : RegExpOpCode.WordBoundary,
                    _ => _unicode && assertion.IgnoreCase ? RegExpOpCode.NotWordBoundaryIgnoreCase : RegExpOpCode.NotWordBoundary,
                });
                break;

            case RegExpGroupNode group:
                _code.Add(RegExpOpCode.Save);
                _code.Add(backward ? (group.Index * 2) + 1 : group.Index * 2);
                Emit(group.Body, backward);
                _code.Add(RegExpOpCode.Save);
                _code.Add(backward ? group.Index * 2 : (group.Index * 2) + 1);
                break;

            case RegExpLookaroundNode lookaround:
                {
                    var index = _lookarounds.Count;
                    var register = _registerCount++;
                    _lookarounds.Add(default);
                    _code.Add(RegExpOpCode.LookStart);
                    _code.Add(index);
                    Emit(lookaround.Body, lookaround.Behind);
                    _code.Add(RegExpOpCode.LookEnd);
                    _code.Add(index);
                    _lookarounds[index] = new RegExpLookaroundInfo(lookaround.Negative, register, lookaround.FirstGroup * 2, lookaround.GroupCount * 2, Position);
                    break;
                }

            case RegExpBackreferenceNode backreference:
                _code.Add(RegExpOpCode.Backreference);
                _code.Add(_backreferences.Count);
                _backreferences.Add(new RegExpBackreferenceInfo(backreference.Groups, backreference.IgnoreCase, backward));
                break;

            case RegExpQuantifierNode quantifier:
                EmitQuantifier(quantifier, backward);
                break;

            default:
                throw new InvalidOperationException("Unexpected node " + node.GetType().Name);
        }
    }

    private void EmitSequence(RegExpSequenceNode sequence, bool backward)
    {
        var items = sequence.Items;
        var count = items.Length;
        for (var i = 0; i < count; i++)
        {
            var index = backward ? count - 1 - i : i;

            // Merge the consecutive case-sensitive characters into a string
            var runLength = 0;
            while (runLength + i < count && GetLiteralCharacter(items[backward ? count - 1 - (i + runLength) : i + runLength]) >= 0)
            {
                runLength++;
            }

            if (runLength >= 2)
            {
                var builder = new StringBuilder();
                var first = backward ? count - i - runLength : i;
                for (var j = first; j < first + runLength; j++)
                {
                    var value = GetLiteralCharacter(items[j]);
                    if (value > 0xFFFF)
                    {
                        builder.Append(char.ConvertFromUtf32(value));
                    }
                    else
                    {
                        builder.Append((char)value);
                    }
                }

                _code.Add(backward ? RegExpOpCode.StringBack : RegExpOpCode.String);
                _code.Add(_strings.Count);
                _strings.Add(builder.ToString());
                i += runLength - 1;
                continue;
            }

            Emit(items[index], backward);
        }
    }

    /// <summary>Gets the value of a case-sensitive character that can be part of a string literal, or -1.</summary>
    private int GetLiteralCharacter(RegExpNode node)
    {
        if (node is not RegExpCharNode { IgnoreCase: false } character)
            return -1;

        var value = character.Value;
        if (value is >= 0xD800 and <= 0xDFFF && _unicode)
            return -1; // a lone surrogate must not match half of a pair

        return value;
    }

    private void EmitAlternation(RegExpAlternationNode alternation, bool backward)
    {
        var alternatives = alternation.Alternatives;
        var jumps = new List<int>();
        for (var i = 0; i < alternatives.Length - 1; i++)
        {
            _code.Add(RegExpOpCode.PushAlternative);
            var patch = Position;
            _code.Add(0);
            Emit(alternatives[i], backward);
            _code.Add(RegExpOpCode.Jump);
            jumps.Add(Position);
            _code.Add(0);
            _code[patch] = Position;
        }

        Emit(alternatives[^1], backward);
        foreach (var jump in jumps)
        {
            _code[jump] = Position;
        }
    }

    private static bool IsSingleCharacterNode(RegExpNode node) => node is RegExpCharNode or RegExpSetNode or RegExpDotNode;

    private void EmitSingleCharacter(RegExpNode node, bool backward)
    {
        switch (node)
        {
            case RegExpCharNode character:
                EmitCharacter(character.Value, character.IgnoreCase, backward);
                break;

            case RegExpSetNode set:
                {
                    var matchSet = set.IgnoreCase ? RegExpCaseFolding.AddCaseEquivalents(set.Set, _unicode) : set.Set;
                    if (set.Invert)
                    {
                        matchSet = matchSet.Complement(_unicode ? CodePointSet.MaxCodePoint : 0xFFFF);
                    }

                    // A set without surrogates and supplementary characters matches the same code units in both modes
                    var codePoints = _unicode && matchSet.RangeCount > 0 && matchSet.GetRangeEnd(matchSet.RangeCount - 1) >= 0xD800 && !IsOnlyBmpWithoutSurrogates(matchSet);
                    var opCode = codePoints
                        ? (backward ? RegExpOpCode.ClassCodePointBack : RegExpOpCode.ClassCodePoint)
                        : (backward ? RegExpOpCode.ClassBack : RegExpOpCode.Class);
                    _code.Add(opCode);
                    _code.Add(GetClassIndex(matchSet));
                    break;
                }

            case RegExpDotNode dot:
                if (_unicode)
                {
                    _code.Add(dot.DotAll ? (backward ? RegExpOpCode.AnyCodePointBack : RegExpOpCode.AnyCodePoint) : (backward ? RegExpOpCode.DotCodePointBack : RegExpOpCode.DotCodePoint));
                }
                else
                {
                    _code.Add(dot.DotAll ? (backward ? RegExpOpCode.AnyBack : RegExpOpCode.Any) : (backward ? RegExpOpCode.DotBack : RegExpOpCode.Dot));
                }

                break;
        }
    }

    private static bool IsOnlyBmpWithoutSurrogates(CodePointSet set)
    {
        var count = set.RangeCount;
        for (var i = 0; i < count; i++)
        {
            var start = set.GetRangeStart(i);
            var end = set.GetRangeEnd(i);
            if (end > 0xFFFF || (start <= 0xDFFF && end >= 0xD800))
                return false;
        }

        return true;
    }

    private void EmitCharacter(int value, bool ignoreCase, bool backward)
    {
        if (ignoreCase)
        {
            var equivalents = RegExpCaseFolding.AddCaseEquivalents(CodePointSet.Single(value), _unicode);
            if (!equivalents.TryGetSingle(out _))
            {
                if (_unicode)
                {
                    _code.Add(backward ? RegExpOpCode.CharCodePointIgnoreCaseBack : RegExpOpCode.CharCodePointIgnoreCase);
                    _code.Add(RegExpCaseFolding.CanonicalizeUnicode(value));
                }
                else
                {
                    _code.Add(backward ? RegExpOpCode.CharIgnoreCaseBack : RegExpOpCode.CharIgnoreCase);
                    _code.Add(RegExpCaseFolding.CanonicalizeNonUnicode((char)value));
                }

                return;
            }
        }

        if (_unicode && (value > 0xFFFF || value is >= 0xD800 and <= 0xDFFF))
        {
            _code.Add(backward ? RegExpOpCode.CharCodePointBack : RegExpOpCode.CharCodePoint);
        }
        else
        {
            _code.Add(backward ? RegExpOpCode.CharBack : RegExpOpCode.Char);
        }

        _code.Add(value);
    }

    private int GetClassIndex(CodePointSet set)
    {
        var index = _classes.Count;
        _classes.Add(new RegExpCharClass(set));
        return index;
    }

    private void EmitQuantifier(RegExpQuantifierNode quantifier, bool backward)
    {
        var body = quantifier.Body;
        var min = quantifier.Min;
        var max = quantifier.Max;
        if (max == 0)
            return; // RepeatMatcher: "If max = 0, return c(x)"

        if (min == 1 && max == 1)
        {
            // A single mandatory iteration: no capture to clear, no empty check
            Emit(body, backward);
            return;
        }

        if (IsSingleCharacterNode(body))
        {
            _code.Add(quantifier.Greedy ? RegExpOpCode.StarGreedy : RegExpOpCode.StarLazy);
            _code.Add(min);
            _code.Add(max);
            var patch = Position;
            _code.Add(0);
            EmitSingleCharacter(body, backward);
            _code[patch] = Position;
            return;
        }

        var bodyCanBeEmpty = body.MinLength == 0;
        if (min == 0 && max == 1 && !bodyCanBeEmpty)
        {
            // An optional atom: the groups it contains are still undefined, as it runs at most once
            _code.Add(quantifier.Greedy ? RegExpOpCode.PushAlternative : RegExpOpCode.PushNextAndJump);
            var patch = Position;
            _code.Add(0);
            Emit(body, backward);
            _code[patch] = Position;
            return;
        }

        if (max == RegExpQuantifierNode.Infinite && min <= 1 && !bodyCanBeEmpty && quantifier.GroupCount == 0)
        {
            // a* or a+ of an atom that consumes characters and has no group: no counter, no empty check
            if (min == 1)
            {
                Emit(body, backward);
            }

            // Greedy: run the body, and exit when backtracking; lazy: exit, and run the body when backtracking
            var loop = Position;
            _code.Add(quantifier.Greedy ? RegExpOpCode.PushAlternative : RegExpOpCode.PushNextAndJump);
            var exit = Position;
            _code.Add(0);
            Emit(body, backward);
            _code.Add(RegExpOpCode.Jump);
            _code.Add(loop);
            _code[exit] = Position;
            return;
        }

        // The general RepeatMatcher
        var register = _registerCount;
        _registerCount += 2;
        _code.Add(RegExpOpCode.RepeatInit);
        _code.Add(register);
        var branch = Position;
        _code.Add(RegExpOpCode.RepeatBranch);
        _code.Add(register);
        _code.Add(min);
        _code.Add(max);
        _code.Add(quantifier.Greedy ? 1 : 0);
        var exitPatch = Position;
        _code.Add(0);
        _code.Add(RegExpOpCode.RepeatIteration);
        _code.Add(register);
        _code.Add(quantifier.FirstGroup * 2);
        _code.Add(quantifier.GroupCount * 2);
        Emit(body, backward);
        _code.Add(RegExpOpCode.RepeatEnd);
        _code.Add(register);
        _code.Add(min);
        _code.Add(bodyCanBeEmpty ? 1 : 0);
        _code.Add(branch);
        _code[exitPatch] = Position;
    }
}

/// <summary>A lookaround of a program: its polarity, the register that holds its marker, the capture slots it contains, and the instruction after it.</summary>
internal readonly record struct RegExpLookaroundInfo(bool Negative, int Register, int FirstSlot, int SlotCount, int Continuation);

/// <summary>A backreference of a program: the groups it refers to (several for a duplicate name).</summary>
internal sealed record RegExpBackreferenceInfo(int[] Groups, bool IgnoreCase, bool Backward);
