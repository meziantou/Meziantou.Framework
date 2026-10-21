namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The instructions of a compiled pattern. An instruction is an opcode followed by its operands in the same
/// <c>int[]</c>. The "Back" variants match backward (in a lookbehind); the "CodePoint" variants read a code point (the
/// "u" and "v" flags) rather than a code unit.
/// </summary>
internal static class RegExpOpCode
{
    /// <summary>The pattern matched.</summary>
    public const int Match = 0;

    /// <summary>Backtracks.</summary>
    public const int Fail = 1;

    /// <summary><c>Jump target</c></summary>
    public const int Jump = 2;

    /// <summary><c>PushAlternative target</c>: continues with the next instruction, and tries <c>target</c> when backtracking.</summary>
    public const int PushAlternative = 3;

    /// <summary><c>PushNextAndJump target</c>: continues at <c>target</c>, and tries the next instruction when backtracking.</summary>
    public const int PushNextAndJump = 4;

    // Single-character matchers: an instruction that consumes exactly one character (see IsSingleCharacter)
    public const int Char = 10;
    public const int CharBack = 11;
    public const int CharIgnoreCase = 12;
    public const int CharIgnoreCaseBack = 13;
    public const int CharCodePoint = 14;
    public const int CharCodePointBack = 15;
    public const int CharCodePointIgnoreCase = 16;
    public const int CharCodePointIgnoreCaseBack = 17;
    public const int Class = 18;
    public const int ClassBack = 19;
    public const int ClassCodePoint = 20;
    public const int ClassCodePointBack = 21;
    public const int Dot = 22;
    public const int DotBack = 23;
    public const int DotCodePoint = 24;
    public const int DotCodePointBack = 25;
    public const int Any = 26;
    public const int AnyBack = 27;
    public const int AnyCodePoint = 28;
    public const int AnyCodePointBack = 29;

    /// <summary><c>String index</c>: a case-sensitive literal.</summary>
    public const int String = 40;
    public const int StringBack = 41;

    public const int AssertStart = 50;
    public const int AssertStartLine = 51;
    public const int AssertEnd = 52;
    public const int AssertEndLine = 53;
    public const int WordBoundary = 54;
    public const int NotWordBoundary = 55;
    public const int WordBoundaryIgnoreCase = 56;
    public const int NotWordBoundaryIgnoreCase = 57;

    /// <summary><c>Save slot</c>: stores the position in a capture slot (2 per group: start, end).</summary>
    public const int Save = 60;

    /// <summary><c>RepeatInit register</c>: starts a quantifier; the register holds the count, the next one the start of the iteration.</summary>
    public const int RepeatInit = 61;

    /// <summary><c>RepeatBranch register min max greedy exit</c>: decides whether to run another iteration.</summary>
    public const int RepeatBranch = 62;

    /// <summary><c>RepeatIteration register firstSlot slotCount</c>: starts an iteration and clears the captures of the atom.</summary>
    public const int RepeatIteration = 63;

    /// <summary><c>RepeatEnd register min checkEmpty branch</c>: ends an iteration.</summary>
    public const int RepeatEnd = 64;

    /// <summary><c>StarGreedy min max next</c> followed by a single-character matcher: a greedy quantifier of one character.</summary>
    public const int StarGreedy = 65;

    /// <summary><c>StarLazy min max next</c> followed by a single-character matcher.</summary>
    public const int StarLazy = 66;

    /// <summary>
    /// <c>StarPossessive min max next</c> followed by a single-character matcher: a greedy quantifier of one character
    /// that never gives a character back, because what follows it cannot start with one of its characters.
    /// </summary>
    public const int StarPossessive = 67;

    /// <summary><c>LookStart index</c></summary>
    public const int LookStart = 70;

    /// <summary><c>LookEnd index</c></summary>
    public const int LookEnd = 71;

    /// <summary><c>Backreference index</c></summary>
    public const int Backreference = 72;

    /// <summary>Gets the number of ints of an instruction.</summary>
    public static int GetLength(int opCode)
    {
        return opCode switch
        {
            Match or Fail => 1,
            Dot or DotBack or DotCodePoint or DotCodePointBack or Any or AnyBack or AnyCodePoint or AnyCodePointBack => 1,
            AssertStart or AssertStartLine or AssertEnd or AssertEndLine => 1,
            WordBoundary or NotWordBoundary or WordBoundaryIgnoreCase or NotWordBoundaryIgnoreCase => 1,
            Jump or PushAlternative or PushNextAndJump => 2,
            >= Char and <= ClassCodePointBack => 2,
            String or StringBack or Save or RepeatInit or LookStart or LookEnd or Backreference => 2,
            RepeatBranch => 6,
            RepeatIteration => 4,
            RepeatEnd => 5,
            StarGreedy or StarLazy or StarPossessive => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(opCode)),
        };
    }

    public static bool IsSingleCharacter(int opCode) => opCode is >= Char and <= AnyCodePointBack;
}
