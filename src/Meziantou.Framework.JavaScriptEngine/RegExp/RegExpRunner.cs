using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Runs a <see cref="RegExpProgram"/>: a backtracking interpreter whose choice points and undo records are on an
/// explicit stack (no .NET recursion, whatever the pattern). One runner is cached per thread.
/// </summary>
internal sealed class RegExpRunner
{
    private const int StepsBetweenBudgetChecks = 1 << 16;
    private const int MaxStackLength = 1 << 25; // 128 MB
    private const int MaxCachedStackLength = 1 << 16;

    // The records of the backtracking stack: the values, then the tag on top
    private const int TagGoto = 0; // pc, pos
    private const int TagRestoreCapture = 1; // slot, value
    private const int TagRestoreRegister = 2; // register, value
    private const int TagStarForward = 3; // next, minPos, pos
    private const int TagStarBackward = 4; // next, minPos, pos
    private const int TagLazy = 5; // instruction, pos, count
    private const int TagLook = 6; // captures..., pos, index
    private const int TagLookDone = 7; // captures..., pos, index

    [ThreadStatic]
    private static RegExpRunner? s_cached;

    private int[] _stack = new int[256];
    private int _sp;
    private int[] _captures = [];
    private int[] _registers = [];
    private int _budget = StepsBetweenBudgetChecks;
    private JsEngine? _engine;
    private long _matchStart;
    private RegExpProgram _program = null!;
    private string _input = "";

    public static RegExpRunner Rent()
    {
        var runner = s_cached;
        if (runner is null)
            return new RegExpRunner();

        s_cached = null;
        return runner;
    }

    public static void Return(RegExpRunner runner)
    {
        // Do not keep the memory of a deep backtracking alive
        if (runner._stack.Length > MaxCachedStackLength)
        {
            runner._stack = new int[256];
        }

        runner._program = null!;
        runner._input = "";
        runner._engine = null;
        s_cached = runner;
    }

    public int[]? Exec(RegExpProgram program, string input, int lastIndex, bool sticky, JsEngine? engine)
    {
        _program = program;
        _input = input;
        _engine = engine;
        _matchStart = engine?.Options.RegExpTimeout is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        var slotCount = (program.GroupCount + 1) * 2;
        if (_captures.Length < slotCount)
        {
            _captures = new int[slotCount];
        }

        if (_registers.Length < program.RegisterCount)
        {
            _registers = new int[program.RegisterCount];
        }

        var length = input.Length;
        var unicode = program.IsUnicode;
        var search = program.Search;
        var index = lastIndex;
        var first = true;
        while (index <= length)
        {
            var start = index;

            // Under "u" or "v", an index inside a surrogate pair designates the character of the pair
            if (first && unicode && start > 0 && start < length && char.IsLowSurrogate(input[start]) && char.IsHighSurrogate(input[start - 1]))
            {
                start--;
            }

            if (!sticky)
            {
                if (search.AnchoredAtStart)
                {
                    if (start > 0)
                        return null;
                }
                else if (search.Prefix is { } prefix)
                {
                    var found = input.AsSpan(start).IndexOf(prefix, StringComparison.Ordinal);
                    if (found < 0)
                        return null;

                    if (found > 0)
                    {
                        start += found;
                        index = start;
                    }
                }
                else if (search.FirstCharacters is { } firstCharacters)
                {
                    var next = start;
                    while (next < length && !firstCharacters.Contains(input[next]))
                    {
                        next = Advance(input, next, unicode);
                    }

                    if (next >= length)
                        return null; // the pattern cannot match the empty string

                    if (next != start)
                    {
                        start = next;
                        index = next;
                    }
                }
            }

            if (Run(start))
            {
                // The start is the index of the first character of the match: when lastIndex is inside a surrogate
                // pair, it is the index of the pair (as V8 does; the specification would report lastIndex, which
                // can be after the end of an empty match)
                var result = new int[slotCount];
                Array.Copy(_captures, result, slotCount);
                return result;
            }

            if (sticky)
                return null;

            if (search.SkipLeadingRun)
            {
                var end = start;
                ConsumeGreedy(program.Code, 4, ref end, int.MaxValue, 0, out _);
                if (end > index)
                {
                    index = end;
                }
            }

            index = Advance(input, index, unicode);
            first = false;
        }

        return null;
    }

    /// <summary>AdvanceStringIndex</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Advance(string input, int index, bool unicode)
    {
        if (unicode && index + 1 < input.Length && char.IsHighSurrogate(input[index]) && char.IsLowSurrogate(input[index + 1]))
            return index + 2;

        return index + 1;
    }

    private bool Run(int start)
    {
        var program = _program;
        var code = program.Code;
        var input = _input;
        var captures = _captures;
        var registers = _registers;
        captures.AsSpan(0, (program.GroupCount + 1) * 2).Fill(-1);
        _sp = 0;
        var pc = 0;
        var pos = start;

        while (true)
        {
            if (--_budget <= 0)
            {
                CheckBudget();
            }

            var opCode = code[pc];
            switch (opCode)
            {
                case RegExpOpCode.Match:
                    captures[0] = start;
                    captures[1] = pos;
                    return true;

                case RegExpOpCode.Fail:
                    break;

                case RegExpOpCode.Jump:
                    pc = code[pc + 1];
                    continue;

                case RegExpOpCode.PushAlternative:
                    Push(code[pc + 1], pos, TagGoto);
                    pc += 2;
                    continue;

                case RegExpOpCode.PushNextAndJump:
                    Push(pc + 2, pos, TagGoto);
                    pc = code[pc + 1];
                    continue;

                case RegExpOpCode.Char:
                    if ((uint)pos < (uint)input.Length && input[pos] == code[pc + 1])
                    {
                        pos++;
                        pc += 2;
                        continue;
                    }

                    break;

                case RegExpOpCode.String:
                    {
                        var value = program.Strings[code[pc + 1]];
                        if (pos <= input.Length - value.Length && input.AsSpan(pos, value.Length).SequenceEqual(value))
                        {
                            pos += value.Length;
                            pc += 2;
                            continue;
                        }

                        break;
                    }

                case RegExpOpCode.StringBack:
                    {
                        var value = program.Strings[code[pc + 1]];
                        if (pos >= value.Length && input.AsSpan(pos - value.Length, value.Length).SequenceEqual(value))
                        {
                            pos -= value.Length;
                            pc += 2;
                            continue;
                        }

                        break;
                    }

                case RegExpOpCode.AssertStart:
                    if (pos == 0)
                    {
                        pc++;
                        continue;
                    }

                    break;

                case RegExpOpCode.AssertStartLine:
                    if (pos == 0 || IsLineTerminator(input[pos - 1]))
                    {
                        pc++;
                        continue;
                    }

                    break;

                case RegExpOpCode.AssertEnd:
                    if (pos == input.Length)
                    {
                        pc++;
                        continue;
                    }

                    break;

                case RegExpOpCode.AssertEndLine:
                    if (pos == input.Length || IsLineTerminator(input[pos]))
                    {
                        pc++;
                        continue;
                    }

                    break;

                case RegExpOpCode.WordBoundary:
                case RegExpOpCode.NotWordBoundary:
                case RegExpOpCode.WordBoundaryIgnoreCase:
                case RegExpOpCode.NotWordBoundaryIgnoreCase:
                    {
                        var extra = opCode is RegExpOpCode.WordBoundaryIgnoreCase or RegExpOpCode.NotWordBoundaryIgnoreCase;
                        var a = pos > 0 && IsWordCharacter(input[pos - 1], extra);
                        var b = pos < input.Length && IsWordCharacter(input[pos], extra);
                        var boundary = a != b;
                        if (boundary == opCode is RegExpOpCode.WordBoundary or RegExpOpCode.WordBoundaryIgnoreCase)
                        {
                            pc++;
                            continue;
                        }

                        break;
                    }

                case RegExpOpCode.Save:
                    {
                        var slot = code[pc + 1];
                        Push(slot, captures[slot], TagRestoreCapture);
                        captures[slot] = pos;
                        pc += 2;
                        continue;
                    }

                case RegExpOpCode.RepeatInit:
                    {
                        var register = code[pc + 1];
                        Push(register, registers[register], TagRestoreRegister);
                        Push(register + 1, registers[register + 1], TagRestoreRegister);
                        registers[register] = 0;
                        pc += 2;
                        continue;
                    }

                case RegExpOpCode.RepeatBranch:
                    {
                        var count = registers[code[pc + 1]];
                        if (count < code[pc + 2])
                        {
                            pc += 6;
                            continue;
                        }

                        var max = code[pc + 3];
                        if (max != RegExpQuantifierNode.Infinite && count >= max)
                        {
                            pc = code[pc + 5];
                            continue;
                        }

                        if (code[pc + 4] != 0)
                        {
                            Push(code[pc + 5], pos, TagGoto);
                            pc += 6;
                        }
                        else
                        {
                            Push(pc + 6, pos, TagGoto);
                            pc = code[pc + 5];
                        }

                        continue;
                    }

                case RegExpOpCode.RepeatIteration:
                    {
                        var register = code[pc + 1] + 1;
                        Push(register, registers[register], TagRestoreRegister);
                        registers[register] = pos;
                        var firstSlot = code[pc + 2];
                        var end = firstSlot + code[pc + 3];
                        for (var slot = firstSlot; slot < end; slot++)
                        {
                            if (captures[slot] != -1)
                            {
                                Push(slot, captures[slot], TagRestoreCapture);
                                captures[slot] = -1;
                            }
                        }

                        pc += 4;
                        continue;
                    }

                case RegExpOpCode.RepeatEnd:
                    {
                        var register = code[pc + 1];
                        var count = registers[register];
                        if (code[pc + 3] != 0 && count >= code[pc + 2] && pos == registers[register + 1])
                            break; // an optional iteration that matched the empty string

                        Push(register, count, TagRestoreRegister);
                        registers[register] = count + 1;
                        pc = code[pc + 4];
                        continue;
                    }

                case RegExpOpCode.StarGreedy:
                case RegExpOpCode.StarPossessive:
                    {
                        var min = code[pc + 1];
                        var characterPc = pc + 4;
                        var count = ConsumeGreedy(code, characterPc, ref pos, code[pc + 2], min, out var minPos);
                        _budget -= count;
                        if (count < min)
                            break;

                        if (count > min && opCode == RegExpOpCode.StarGreedy)
                        {
                            Push(code[pc + 3], minPos, pos, (code[characterPc] & 1) != 0 ? TagStarBackward : TagStarForward);
                        }

                        pc = code[pc + 3];
                        continue;
                    }

                case RegExpOpCode.StarLazy:
                    {
                        var min = code[pc + 1];
                        var characterPc = pc + 4;
                        var count = 0;
                        var matched = true;
                        while (count < min)
                        {
                            if (!TryStep(code, characterPc, ref pos))
                            {
                                matched = false;
                                break;
                            }

                            count++;
                        }

                        _budget -= count;
                        if (!matched)
                            break;

                        if (count < code[pc + 2])
                        {
                            Push(pc, pos, count, TagLazy);
                        }

                        pc = code[pc + 3];
                        continue;
                    }

                case RegExpOpCode.LookStart:
                    {
                        var index = code[pc + 1];
                        var info = program.Lookarounds[index];
                        EnsureStack(info.SlotCount + 3);
                        var stack = _stack;
                        Array.Copy(captures, info.FirstSlot, stack, _sp, info.SlotCount);
                        _sp += info.SlotCount;
                        stack[_sp] = pos;
                        stack[_sp + 1] = index;
                        stack[_sp + 2] = TagLook;
                        _sp += 3;
                        registers[info.Register] = _sp;
                        pc += 2;
                        continue;
                    }

                case RegExpOpCode.LookEnd:
                    {
                        var info = program.Lookarounds[code[pc + 1]];
                        var markerTop = registers[info.Register];
                        var stack = _stack;
                        var savedPos = stack[markerTop - 3];
                        if (!info.Negative)
                        {
                            // No backtracking into a lookaround: drop its choice points, but keep a record that
                            // restores the captures it set when backtracking past it
                            if (info.SlotCount == 0)
                            {
                                _sp = markerTop - 3;
                            }
                            else
                            {
                                _sp = markerTop;
                                stack[_sp - 1] = TagLookDone;
                            }

                            pos = savedPos;
                            pc += 2;
                            continue;
                        }

                        // The body of a negative lookaround matched: the assertion fails
                        var snapshot = markerTop - 3 - info.SlotCount;
                        Array.Copy(stack, snapshot, captures, info.FirstSlot, info.SlotCount);
                        _sp = snapshot;
                        break;
                    }

                case RegExpOpCode.Backreference:
                    if (MatchBackreference(program.Backreferences[code[pc + 1]], ref pos))
                    {
                        pc += 2;
                        continue;
                    }

                    break;

                default:
                    if (TryStep(code, pc, ref pos))
                    {
                        pc += RegExpOpCode.IsSingleCharacter(opCode) && opCode is >= RegExpOpCode.Dot and <= RegExpOpCode.AnyCodePointBack ? 1 : 2;
                        continue;
                    }

                    break;
            }

            // Backtrack: undo the records until a choice point
            if (!Backtrack(code, ref pc, ref pos))
                return false;
        }
    }

    private bool Backtrack(int[] code, ref int pc, ref int pos)
    {
        var captures = _captures;
        var registers = _registers;
        while (true)
        {
            var sp = _sp;
            if (sp == 0)
                return false;

            if (--_budget <= 0)
            {
                CheckBudget();
            }

            var stack = _stack;
            switch (stack[sp - 1])
            {
                case TagGoto:
                    pc = stack[sp - 3];
                    pos = stack[sp - 2];
                    _sp = sp - 3;
                    return true;

                case TagRestoreCapture:
                    captures[stack[sp - 3]] = stack[sp - 2];
                    _sp = sp - 3;
                    continue;

                case TagRestoreRegister:
                    registers[stack[sp - 3]] = stack[sp - 2];
                    _sp = sp - 3;
                    continue;

                case TagStarForward:
                    {
                        var minPos = stack[sp - 3];
                        var current = stack[sp - 2];
                        current--;
                        if (_program.IsUnicode && current > minPos && char.IsLowSurrogate(_input[current]) && char.IsHighSurrogate(_input[current - 1]))
                        {
                            current--;
                        }

                        pc = stack[sp - 4];
                        pos = current;
                        if (current <= minPos)
                        {
                            _sp = sp - 4;
                        }
                        else
                        {
                            stack[sp - 2] = current;
                        }

                        return true;
                    }

                case TagStarBackward:
                    {
                        var minPos = stack[sp - 3];
                        var current = stack[sp - 2];
                        current++;
                        if (_program.IsUnicode && current < minPos && char.IsHighSurrogate(_input[current - 1]) && char.IsLowSurrogate(_input[current]))
                        {
                            current++;
                        }

                        pc = stack[sp - 4];
                        pos = current;
                        if (current >= minPos)
                        {
                            _sp = sp - 4;
                        }
                        else
                        {
                            stack[sp - 2] = current;
                        }

                        return true;
                    }

                case TagLazy:
                    {
                        var instruction = stack[sp - 4];
                        var current = stack[sp - 3];
                        if (!TryStep(code, instruction + 4, ref current))
                        {
                            _sp = sp - 4;
                            continue;
                        }

                        var count = stack[sp - 2] + 1;
                        if (count < code[instruction + 2])
                        {
                            stack[sp - 3] = current;
                            stack[sp - 2] = count;
                        }
                        else
                        {
                            _sp = sp - 4;
                        }

                        pc = code[instruction + 3];
                        pos = current;
                        return true;
                    }

                case TagLook:
                    {
                        // The body of the lookaround did not match
                        var info = _program.Lookarounds[stack[sp - 2]];
                        var savedPos = stack[sp - 3];
                        _sp = sp - 3 - info.SlotCount;
                        if (info.Negative)
                        {
                            pc = info.Continuation;
                            pos = savedPos;
                            return true;
                        }

                        continue;
                    }

                case TagLookDone:
                    {
                        var info = _program.Lookarounds[stack[sp - 2]];
                        var snapshot = sp - 3 - info.SlotCount;
                        Array.Copy(stack, snapshot, captures, info.FirstSlot, info.SlotCount);
                        _sp = snapshot;
                        continue;
                    }

                default:
                    throw new InvalidOperationException("Invalid backtracking record");
            }
        }
    }

    /// <summary>Matches a single-character instruction as many times as possible, at most <paramref name="max"/> times.</summary>
    /// <returns>The number of characters matched.</returns>
    private int ConsumeGreedy(int[] code, int pc, ref int pos, int max, int min, out int minPos)
    {
        var input = _input;
        var start = pos;
        var available = input.Length - pos;
        int count;
        switch (code[pc])
        {
            // The forward matchers of code units: a tight loop, and the position after min characters is known
            case RegExpOpCode.Char:
                {
                    var c = (char)code[pc + 1];
                    var limit = (int)Math.Min((uint)max, (uint)available);
                    var span = input.AsSpan(pos, limit);
                    count = span.IndexOfAnyExcept(c);
                    if (count < 0)
                    {
                        count = limit;
                    }

                    break;
                }

            case RegExpOpCode.Class:
                {
                    var characterClass = _program.Classes[code[pc + 1]];
                    var limit = (int)Math.Min((uint)max, (uint)available);
                    count = 0;
                    while (count < limit && characterClass.Contains(input[pos + count]))
                    {
                        count++;
                    }

                    break;
                }

            case RegExpOpCode.Dot:
                {
                    var limit = (int)Math.Min((uint)max, (uint)available);
                    count = input.AsSpan(pos, limit).IndexOfAny("\n\r\u2028\u2029");
                    if (count < 0)
                    {
                        count = limit;
                    }

                    break;
                }

            case RegExpOpCode.Any:
                count = (int)Math.Min((uint)max, (uint)available);
                break;

            default:
                {
                    // Code points, backward matching: one character at a time
                    minPos = pos;
                    count = 0;
                    while (count < max && TryStep(code, pc, ref pos))
                    {
                        count++;
                        if (count == min)
                        {
                            minPos = pos;
                        }
                    }

                    return count;
                }
        }

        pos = start + count;
        minPos = start + Math.Min(min, count);
        return count;
    }

    /// <summary>Matches a single-character instruction at <paramref name="pc"/>.</summary>
    private bool TryStep(int[] code, int pc, ref int pos)
    {
        var input = _input;
        switch (code[pc])
        {
            case RegExpOpCode.Char:
                if ((uint)pos < (uint)input.Length && input[pos] == code[pc + 1])
                {
                    pos++;
                    return true;
                }

                return false;

            case RegExpOpCode.CharBack:
                if (pos > 0 && input[pos - 1] == code[pc + 1])
                {
                    pos--;
                    return true;
                }

                return false;

            case RegExpOpCode.CharIgnoreCase:
                if ((uint)pos < (uint)input.Length && RegExpCaseFolding.CanonicalizeNonUnicode(input[pos]) == code[pc + 1])
                {
                    pos++;
                    return true;
                }

                return false;

            case RegExpOpCode.CharIgnoreCaseBack:
                if (pos > 0 && RegExpCaseFolding.CanonicalizeNonUnicode(input[pos - 1]) == code[pc + 1])
                {
                    pos--;
                    return true;
                }

                return false;

            case RegExpOpCode.CharCodePoint:
                {
                    if (pos < input.Length && ReadCodePoint(input, pos, out var size) == code[pc + 1])
                    {
                        pos += size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.CharCodePointBack:
                {
                    if (pos > 0 && ReadCodePointBackward(input, pos, out var size) == code[pc + 1])
                    {
                        pos -= size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.CharCodePointIgnoreCase:
                {
                    if (pos < input.Length && RegExpCaseFolding.CanonicalizeUnicode(ReadCodePoint(input, pos, out var size)) == code[pc + 1])
                    {
                        pos += size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.CharCodePointIgnoreCaseBack:
                {
                    if (pos > 0 && RegExpCaseFolding.CanonicalizeUnicode(ReadCodePointBackward(input, pos, out var size)) == code[pc + 1])
                    {
                        pos -= size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.Class:
                if ((uint)pos < (uint)input.Length && _program.Classes[code[pc + 1]].Contains(input[pos]))
                {
                    pos++;
                    return true;
                }

                return false;

            case RegExpOpCode.ClassBack:
                if (pos > 0 && _program.Classes[code[pc + 1]].Contains(input[pos - 1]))
                {
                    pos--;
                    return true;
                }

                return false;

            case RegExpOpCode.ClassCodePoint:
                {
                    if (pos < input.Length && _program.Classes[code[pc + 1]].Contains(ReadCodePoint(input, pos, out var size)))
                    {
                        pos += size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.ClassCodePointBack:
                {
                    if (pos > 0 && _program.Classes[code[pc + 1]].Contains(ReadCodePointBackward(input, pos, out var size)))
                    {
                        pos -= size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.Dot:
                if ((uint)pos < (uint)input.Length && !IsLineTerminator(input[pos]))
                {
                    pos++;
                    return true;
                }

                return false;

            case RegExpOpCode.DotBack:
                if (pos > 0 && !IsLineTerminator(input[pos - 1]))
                {
                    pos--;
                    return true;
                }

                return false;

            case RegExpOpCode.DotCodePoint:
                {
                    if (pos < input.Length && !IsLineTerminator(ReadCodePoint(input, pos, out var size)))
                    {
                        pos += size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.DotCodePointBack:
                {
                    if (pos > 0 && !IsLineTerminator(ReadCodePointBackward(input, pos, out var size)))
                    {
                        pos -= size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.Any:
                if (pos < input.Length)
                {
                    pos++;
                    return true;
                }

                return false;

            case RegExpOpCode.AnyBack:
                if (pos > 0)
                {
                    pos--;
                    return true;
                }

                return false;

            case RegExpOpCode.AnyCodePoint:
                {
                    if (pos < input.Length)
                    {
                        ReadCodePoint(input, pos, out var size);
                        pos += size;
                        return true;
                    }

                    return false;
                }

            case RegExpOpCode.AnyCodePointBack:
                {
                    if (pos > 0)
                    {
                        ReadCodePointBackward(input, pos, out var size);
                        pos -= size;
                        return true;
                    }

                    return false;
                }

            default:
                throw new InvalidOperationException("Invalid instruction " + code[pc]);
        }
    }

    /// <summary>BackreferenceMatcher</summary>
    private bool MatchBackreference(RegExpBackreferenceInfo info, ref int pos)
    {
        var captures = _captures;
        var start = -1;
        var end = -1;
        foreach (var group in info.Groups)
        {
            if (captures[group * 2] >= 0 && captures[(group * 2) + 1] >= 0)
            {
                start = captures[group * 2];
                end = captures[(group * 2) + 1];
                break;
            }
        }

        // A group that did not take part in the match matches the empty string
        if (start < 0 || end <= start)
            return true;

        var input = _input;
        var length = end - start;
        var target = info.Backward ? pos - length : pos;
        if (target < 0 || target + length > input.Length)
            return false;

        var unicode = _program.IsUnicode;
        if (unicode)
        {
            // The text must not split a surrogate pair of the input
            if (target > 0 && char.IsLowSurrogate(input[target]) && char.IsHighSurrogate(input[target - 1]))
                return false;

            if (target + length < input.Length && char.IsHighSurrogate(input[target + length - 1]) && char.IsLowSurrogate(input[target + length]))
                return false;
        }

        var captured = input.AsSpan(start, length);
        var candidate = input.AsSpan(target, length);
        if (!info.IgnoreCase)
        {
            if (!captured.SequenceEqual(candidate))
                return false;
        }
        else if (!unicode)
        {
            for (var i = 0; i < length; i++)
            {
                if (RegExpCaseFolding.CanonicalizeNonUnicode(captured[i]) != RegExpCaseFolding.CanonicalizeNonUnicode(candidate[i]))
                    return false;
            }
        }
        else
        {
            var i = 0;
            while (i < length)
            {
                var a = ReadCodePoint(captured, i, out var sizeA);
                var b = ReadCodePoint(candidate, i, out var sizeB);
                if (sizeA != sizeB || RegExpCaseFolding.CanonicalizeUnicode(a) != RegExpCaseFolding.CanonicalizeUnicode(b))
                    return false;

                i += sizeA;
            }
        }

        pos = info.Backward ? target : target + length;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadCodePoint(ReadOnlySpan<char> input, int pos, out int size)
    {
        var c = input[pos];
        if (char.IsHighSurrogate(c) && pos + 1 < input.Length && char.IsLowSurrogate(input[pos + 1]))
        {
            size = 2;
            return char.ConvertToUtf32(c, input[pos + 1]);
        }

        size = 1;
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadCodePointBackward(ReadOnlySpan<char> input, int pos, out int size)
    {
        var c = input[pos - 1];
        if (char.IsLowSurrogate(c) && pos >= 2 && char.IsHighSurrogate(input[pos - 2]))
        {
            size = 2;
            return char.ConvertToUtf32(input[pos - 2], c);
        }

        size = 1;
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLineTerminator(int c) => c is '\n' or '\r' or '\u2028' or '\u2029';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWordCharacter(char c, bool unicodeIgnoreCase)
    {
        return RegExpParser.IsBasicWordCharacter(c) || (unicodeIgnoreCase && c is '\u017F' or '\u212A');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(int a, int b, int tag)
    {
        var sp = _sp;
        var stack = _stack;
        if (sp + 3 > stack.Length)
        {
            stack = Grow(3);
        }

        stack[sp] = a;
        stack[sp + 1] = b;
        stack[sp + 2] = tag;
        _sp = sp + 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(int a, int b, int c, int tag)
    {
        var sp = _sp;
        var stack = _stack;
        if (sp + 4 > stack.Length)
        {
            stack = Grow(4);
        }

        stack[sp] = a;
        stack[sp + 1] = b;
        stack[sp + 2] = c;
        stack[sp + 3] = tag;
        _sp = sp + 4;
    }

    private void EnsureStack(int count)
    {
        if (_sp + count > _stack.Length)
        {
            Grow(count);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int[] Grow(int count)
    {
        var length = Math.Max(_stack.Length * 2, _sp + count);
        if (length > MaxStackLength)
        {
            ThrowStackOverflow();
        }

        Array.Resize(ref _stack, length);
        return _stack;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckBudget()
    {
        _budget = StepsBetweenBudgetChecks;
        _engine?.CheckRegExpBudget(_matchStart);
    }

    private static void ThrowStackOverflow()
    {
        if (JsEngine.Current?.CurrentRealm is not null)
        {
            ThrowHelper.ThrowRangeError("Maximum call stack size exceeded (regular expression too complex)");
        }

        throw new InvalidOperationException("The regular expression is too complex");
    }
}
