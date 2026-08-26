// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
//
// Changes: the table and its predicates are copied unchanged; the members that built RegexNode trees are not here.

using System.Globalization;

namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Classifies ASCII characters the way the .NET engine does.</summary>
/// <remarks>
/// The table decides where a run of ordinary characters stops, which characters begin a quantifier, and which count as
/// whitespace in extended mode. It is copied from the engine rather than rewritten, because a single wrong entry is a
/// silent divergence that only the hardest inputs would expose.
/// </remarks>
internal static class RegexCharacterTables
{
    private const byte Q = 4;    // quantifier          * + ? {
    private const byte S = 3;    // stopper             $ ( ) . [ \ ^ |
    private const byte Z = 2;    // # stopper           #
    private const byte W = 1;    // whitespace          \t \n \f \r ' '

    /// <summary>For categorizing ASCII characters.</summary>
    private static ReadOnlySpan<byte> Category =>
    [
        // 0  1  2  3  4  5  6  7  8  9  A  B  C  D  E  F  0  1  2  3  4  5  6  7  8  9  A  B  C  D  E  F
           0, 0, 0, 0, 0, 0, 0, 0, 0, W, W, W, W, W, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        //    !  "  #  $  %  &  '  (  )  *  +  ,  -  .  /  0  1  2  3  4  5  6  7  8  9  :  ;  <  =  >  ?
           W, 0, 0, Z, S, 0, 0, 0, S, S, Q, Q, 0, 0, S, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Q,
        // @  A  B  C  D  E  F  G  H  I  J  K  L  M  N  O  P  Q  R  S  T  U  V  W  X  Y  Z  [  \  ]  ^  _
           0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, S, S, 0, S, 0,
        // '  a  b  c  d  e  f  g  h  i  j  k  l  m  n  o  p  q  r  s  t  u  v  w  x  y  z  {  |  }  ~
           0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Q, S, 0, 0, 0
    ];

    /// <summary>Returns true for those characters that terminate a string of ordinary chars.</summary>
    public static bool IsSpecial(char ch) => ch <= '|' && Category[ch] >= S;

    /// <summary>Returns true for those characters including whitespace that terminate a string of ordinary chars.</summary>
    public static bool IsSpecialOrSpace(char ch) => ch <= '|' && Category[ch] >= W;

    /// <summary>Returns true for those characters that begin a quantifier.</summary>
    public static bool IsQuantifier(char ch) => ch <= '{' && Category[ch] == Q;

    /// <summary>Returns true for whitespace.</summary>
    public static bool IsSpace(char ch) => ch <= ' ' && Category[ch] == W;

    /// <summary>Returns whether the character is the <c>#</c> that starts an extended-mode comment.</summary>
    public static bool IsCommentStart(char ch) => ch <= '|' && Category[ch] == Z;

    /// <summary>Returns whether the character at <paramref name="position"/> begins a quantifier.</summary>
    /// <remarks>
    /// A <c>{</c> only begins one when a well-formed bound follows it, so this looks ahead without consuming anything.
    /// Deciding before any character is claimed is what lets the parser avoid the engine's rewind, which would move
    /// the reading position backwards and break the spans of everything already built.
    /// </remarks>
    public static bool IsTrueQuantifier(string pattern, int position)
    {
        if (position >= pattern.Length)
            return false;

        var startpos = position;
        var ch = pattern[startpos];
        if (ch != '{')
            return ch <= '{' && Category[ch] >= Q;

        var pos = startpos;
        var nChars = pattern.Length - position;
        while (--nChars > 0 && (uint)((ch = pattern[++pos]) - '0') <= 9)
        {
        }

        if (nChars == 0 || pos - startpos == 1)
            return false;

        if (ch == '}')
            return true;

        if (ch != ',')
            return false;

        while (--nChars > 0 && (uint)((ch = pattern[++pos]) - '0') <= 9)
        {
        }

        return nChars > 0 && ch == '}';
    }

    /// <summary>Returns whether the character is a word character, as the engine's character-class helper defines it.</summary>
    public static bool IsWordChar(char ch) =>
        char.IsAsciiLetterOrDigit(ch) || ch == '_' ||
        (ch > '\u007f' && CharUnicodeInfo.GetUnicodeCategory(ch) is
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation);

    /// <summary>Returns whether the character may appear in a capture-group name.</summary>
    /// <remarks>
    /// The zero-width joiner and non-joiner count for the purpose of a name even though <see cref="IsWordChar"/> does
    /// not accept them, which is what the engine's boundary-word test encodes.
    /// </remarks>
    public static bool IsBoundaryWordChar(char ch) => IsWordChar(ch) || ch is '\u200c' or '\u200d';
}
