using System.Globalization;
using System.Text;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using ScannedToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>Parses an ECMAScript pattern, and the delimiters and flags of a literal when there are any.</summary>
internal sealed class JavaScriptRegexParser : PerlStyleRegexParser
{
    /// <summary>
    /// The punctuators the class set grammar lets a backslash escape inside a class, on top of the syntax characters.
    /// </summary>
    private const string ClassSetReservedPunctuators = "&-!#%,:;<=>@`~";

    private readonly JavaScriptLiteral? _literal;
    private readonly Dictionary<string, List<(int Alternation, int Branch)[]>> _groupNames = new(StringComparer.Ordinal);

    public JavaScriptRegexParser(SourceText source, RegexParseOptions parseOptions, JavaScriptLiteral? literal)
        : base(source, parseOptions)
    {
        _literal = literal;
    }

    /// <summary>
    /// ECMAScript has two grammars, and which one applies is decided by the <c>u</c> and <c>v</c> flags rather than by
    /// the dialect.
    /// </summary>
    /// <remarks>
    /// Without them the web-compatibility grammar applies: an escape that is not well formed stands for its own
    /// letter, so <c>\x4</c> matches <c>x4</c> and <c>\k</c> in a pattern with no named group matches <c>k</c>. With
    /// them the grammar is strict, and only a syntax character or <c>/</c> may follow a backslash and mean itself.
    /// </remarks>
    private bool UsesStrictGrammar => UsesUnicodeMode;

    protected override bool AllowsIdentityEscape(char ch)
    {
        if (!UsesStrictGrammar)
            return true;

        // The strict grammar allows only these, plus "-" inside a character class, and under the "v" flag the
        // punctuators the class set grammar reserves.
        return ch is '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '/'
            || (ch == '-' && IsInCharacterClass)
            || (IsInCharacterClass && UsesUnicodeSetsMode && ClassSetReservedPunctuators.Contains(ch, StringComparison.Ordinal));
    }

    protected override bool HasBellAndEscapeEscapes => false;

    protected override bool UsesJavaScriptDecimalEscapes => true;

    protected override bool ReportsShorthandClassAsRangeStart => UsesStrictGrammar;

    protected override bool NamedGroupsTakeNumbersInOrder => true;

    protected override bool AllowsNumberedGroups => false;

    /// <summary>A quantifier bound may be as large as it likes; the engine clamps it.</summary>
    protected override long? MaxBoundValue => null;

    protected override bool IsGroupNameStartAt(int position) =>
        TryReadIdentifierCodePoint(position, out var codePoint, out _) && IsIdentifierStart(codePoint);

    /// <summary>Reads a group name, which is an identifier that may spell any of its characters as <c>\u</c> escapes.</summary>
    protected override string ReadGroupName()
    {
        var name = new StringBuilder();
        while (TryReadIdentifierCodePoint(Scanner.Position, out var codePoint, out var length) &&
            (name.Length == 0 ? IsIdentifierStart(codePoint) : IsIdentifierPart(codePoint)))
        {
            name.Append(char.ConvertFromUtf32(codePoint));
            Scanner.Position += length;
        }

        return name.ToString();
    }

    /// <summary>Two groups may share a name only when they are in different alternatives, so at most one takes part.</summary>
    protected override void CheckGroupNameDeclaration(string name, int number, TextSpan span)
    {
        var path = CurrentAlternativePath;
        if (!_groupNames.TryGetValue(name, out var declared))
        {
            _groupNames[name] = [path];

            return;
        }

        foreach (var other in declared)
        {
            if (MightBothParticipate(other, path))
            {
                AddDiagnostic(span, RegexDiagnosticIds.DuplicateGroupName, $"The group name '{name}' is already used by a group that can take part in the same match.");
                break;
            }
        }

        declared.Add(path);
    }

    /// <summary>
    /// Checks a <c>\p{…}</c> name against the tables of the specification, which JavaScript compares exactly.
    /// </summary>
    protected override void ValidatePropertyName(TextSpan span, string name, bool negated, bool braced)
    {
        var separator = name.IndexOf('=', StringComparison.Ordinal);
        if (separator >= 0)
        {
            var property = name[..separator];
            var value = name[(separator + 1)..];
            var known = property switch
            {
                "General_Category" or "gc" => UnicodePropertyNames.JavaScriptGeneralCategoryValues.Contains(value),
                "Script" or "sc" or "Script_Extensions" or "scx" => UnicodePropertyNames.JavaScriptScriptValues.Contains(value),
                _ => false,
            };

            if (!known)
            {
                AddDiagnostic(span, RegexDiagnosticIds.UnrecognizedUnicodeProperty, $"Unknown Unicode property '{name}'.");
            }

            return;
        }

        if (UnicodePropertyNames.JavaScriptGeneralCategoryValues.Contains(name) || UnicodePropertyNames.JavaScriptBinaryProperties.Contains(name))
            return;

        if (UnicodePropertyNames.JavaScriptStringProperties.Contains(name))
        {
            if (!UsesUnicodeSetsMode)
            {
                AddDiagnostic(span, RegexDiagnosticIds.UnrecognizedUnicodeProperty, $"The property of strings '{name}' requires the 'v' flag.");
            }
            else if (negated)
            {
                AddDiagnostic(span, RegexDiagnosticIds.NegatedClassContainsStrings, $"The property of strings '{name}' cannot be negated.");
            }
            else
            {
                LastSetMemberMayContainStrings = true;
            }

            return;
        }

        AddDiagnostic(span, RegexDiagnosticIds.UnrecognizedUnicodeProperty, $"Unknown Unicode property '{name}'.");
    }

    /// <summary>
    /// Parses the <c>(?ims-ims:…)</c> modifiers group, the only inline options JavaScript has. It always has a body,
    /// takes only the three letters, and may not name a letter twice.
    /// </summary>
    protected override RegexAtomSyntax? TryParseDialectGroupHeader(ScannedToken openParenToken, int questionStart)
    {
        if (!char.IsAsciiLetter(Scanner.Current) && Scanner.Current != '-')
            return null;

        var questionToken = Scanner.Token(SyntaxKind.QuestionToken, questionStart);
        var optionsStart = Scanner.Position;
        var seen = new HashSet<char>();
        var hasHyphen = false;
        var letters = 0;
        while (char.IsAsciiLetter(Scanner.Current) || Scanner.Current == '-')
        {
            var ch = Scanner.Current;
            var span = new TextSpan(Scanner.Position, 1);
            Scanner.Position++;

            if (ch == '-')
            {
                if (hasHyphen)
                {
                    AddDiagnostic(span, RegexDiagnosticIds.InvalidModifiers, "A modifiers group may contain only one '-'.");
                }

                hasHyphen = true;
                continue;
            }

            var option = ch switch
            {
                'i' => RegexPatternOptions.IgnoreCase,
                'm' => RegexPatternOptions.Multiline,
                's' => RegexPatternOptions.DotAll | RegexPatternOptions.Singleline,
                _ => RegexPatternOptions.None,
            };

            if (option == RegexPatternOptions.None)
            {
                AddDiagnostic(span, RegexDiagnosticIds.InvalidModifiers, $"'{ch}' is not a modifier; only 'i', 'm', and 's' are.");
                continue;
            }

            if (!seen.Add(ch))
            {
                AddDiagnostic(span, RegexDiagnosticIds.InvalidModifiers, $"The modifier '{ch}' is repeated.");
            }

            letters++;
            Options = hasHyphen ? Options & ~option : Options | option;
        }

        var optionsToken = Scanner.Token(SyntaxKind.OptionsToken, optionsStart);
        if (hasHyphen && letters == 0)
        {
            AddDiagnostic(optionsToken.Span, RegexDiagnosticIds.InvalidModifiers, "A modifiers group must add or remove at least one modifier.");
        }

        ScannedToken colonToken = default;
        if (Scanner.Current == ':')
        {
            var colonStart = Scanner.Position;
            Scanner.Position++;
            colonToken = Scanner.Token(SyntaxKind.ColonToken, colonStart);
        }
        else
        {
            // JavaScript has no "(?i)" that applies to the rest of the pattern. The body is still read, so the
            // parenthesis is matched and nothing after it is misread.
            AddDiagnostic(
                TextSpan.FromBounds(openParenToken.Span.Start, Scanner.Position),
                RegexDiagnosticIds.InvalidGroupingConstruct,
                "Invalid group: a modifiers group must be written '(?flags:…)'.");
        }

        var body = ParseAlternation(insideGroup: true);
        var closeParenToken = ReadCloseParen(openParenToken);
        var group = new RegexOptionsGroupSyntax(openParenToken, questionToken, optionsToken, colonToken, body, closeParenToken, Options, body.Options);
        RestoreOptions();

        return group;
    }

    /// <summary>
    /// Reads the code point at <paramref name="position"/> the way an identifier does: a surrogate pair is one code point,
    /// and <c>\uHHHH</c>, a pair of those, and <c>\u{…}</c> all spell one.
    /// </summary>
    private bool TryReadIdentifierCodePoint(int position, out int codePoint, out int length)
    {
        codePoint = 0;
        length = 0;
        if (position >= Text.Length)
            return false;

        var ch = Text[position];
        if (ch != '\\')
        {
            length = char.IsHighSurrogate(ch) && position + 1 < Text.Length && char.IsLowSurrogate(Text[position + 1]) ? 2 : 1;
            codePoint = length == 2 ? char.ConvertToUtf32(ch, Text[position + 1]) : ch;

            return true;
        }

        if (position + 1 >= Text.Length || Text[position + 1] != 'u')
            return false;

        if (position + 2 < Text.Length && Text[position + 2] == '{')
        {
            var end = Text.IndexOf('}', position + 3, StringComparison.Ordinal);
            if (end <= position + 3 || !int.TryParse(Text.AsSpan(position + 3, end - position - 3), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out codePoint) || codePoint > 0x10FFFF)
                return false;

            length = end + 1 - position;

            return true;
        }

        if (!TryReadHex4(position + 2, out var unit))
            return false;

        codePoint = unit;
        length = 6;
        if (char.IsHighSurrogate((char)unit) && position + 12 <= Text.Length && Text[position + 6] == '\\' && Text[position + 7] == 'u' &&
            TryReadHex4(position + 8, out var low) && char.IsLowSurrogate((char)low))
        {
            codePoint = char.ConvertToUtf32((char)unit, (char)low);
            length = 12;
        }

        return true;
    }

    private bool TryReadHex4(int position, out int value)
    {
        value = 0;
        if (position + 4 > Text.Length)
            return false;

        foreach (var ch in Text.AsSpan(position, 4))
        {
            if (!char.IsAsciiHexDigit(ch))
                return false;
        }

        return int.TryParse(Text.AsSpan(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Whether a code point may start an identifier: <c>$</c>, <c>_</c>, or anything with ID_Start.</summary>
    private static bool IsIdentifierStart(int codePoint) =>
        codePoint is '$' or '_' || IsIdStart(codePoint);

    /// <summary>Whether a code point may continue an identifier: <c>$</c>, the two joiners, or anything with ID_Continue.</summary>
    private static bool IsIdentifierPart(int codePoint) =>
        codePoint is '$' or 0x200C or 0x200D || IsIdStart(codePoint) || IsIdContinueOnly(codePoint);

    private static bool IsIdStart(int codePoint)
    {
        if (codePoint < 0x80)
            return char.IsAsciiLetter((char)codePoint);

        // Other_ID_Start, and the one letter Pattern_Syntax takes back.
        if (codePoint is 0x1885 or 0x1886 or 0x2118 or 0x212E or 0x309B or 0x309C)
            return true;

        if (codePoint is 0x2E2F or (>= 0xD800 and <= 0xDFFF))
            return false;

        return CharUnicodeInfo.GetUnicodeCategory(codePoint) is
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;
    }

    private static bool IsIdContinueOnly(int codePoint)
    {
        if (codePoint < 0x80)
            return char.IsAsciiDigit((char)codePoint) || codePoint == '_';

        // Other_ID_Continue.
        if (codePoint is 0x00B7 or 0x0387 or (>= 0x1369 and <= 0x1371) or 0x19DA or 0x30FB or 0xFF65)
            return true;

        return CharUnicodeInfo.GetUnicodeCategory(codePoint) is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation;
    }

    protected override bool AllowsMalformedNumericEscape => !UsesStrictGrammar;

    protected override bool AllowsLoneQuantifierBracket => !UsesStrictGrammar;

    protected override bool AllowsOctalEscape => !UsesStrictGrammar;

    protected override bool AllowsNonLetterControlEscape => !UsesStrictGrammar;

    /// <summary>
    /// An assertion matches nothing, so repeating it means nothing. Lookahead is the one exception, and only in the
    /// web-compatibility grammar; lookbehind is never quantifiable.
    /// </summary>
    protected override bool IsQuantifiable(RegexTermSyntax term) => term switch
    {
        RegexAnchorSyntax => false,
        RegexLookaroundSyntax lookaround => !UsesStrictGrammar && !lookaround.IsLookbehind,
        _ => true,
    };

    protected override bool AllowsShorthandClassInRange => !UsesStrictGrammar;

    protected override bool AllowsUndefinedNamedBackreference => !UsesStrictGrammar;

    protected override ScannedToken ReadLiteralPrefix()
    {
        if (_literal is not { HasOpeningSlash: true })
            return default;

        var start = Scanner.Position;
        Scanner.Position = _literal.BodyStart;

        return Scanner.Token(SyntaxKind.SlashToken, start);
    }

    protected override (ScannedToken CloseSlash, ScannedToken Flags, ScannedToken Trailing) ReadLiteralSuffix()
    {
        if (_literal is { LineTerminatorPosition: >= 0 } broken)
        {
            AddDiagnostic(
                new TextSpan(broken.LineTerminatorPosition, 1),
                RegexDiagnosticIds.LineTerminatorInLiteral,
                "A regular-expression literal cannot contain a line terminator.");

            return (default, default, ReadTrailingContent());
        }

        if (_literal is not { HasClosingSlash: true })
        {
            // An opening delimiter with nothing to close it is not a literal at all. The tree still covers the text so
            // it round-trips, but saying nothing about it would be wrong.
            if (_literal is { HasOpeningSlash: true })
            {
                AddDiagnostic(
                    new TextSpan(0, Math.Min(1, Text.Length)),
                    RegexDiagnosticIds.UnterminatedLiteral,
                    "Unterminated regular-expression literal: expected a closing '/'.");
            }

            return (default, default, default);
        }

        var slashStart = Scanner.Position;
        if (slashStart >= Text.Length || Text[slashStart] != '/')
            return (default, default, default);

        Scanner.Position++;
        var closeSlashToken = Scanner.Token(SyntaxKind.SlashToken, slashStart);

        ScannedToken flagsToken = default;
        if (Scanner.Position < _literal.FlagsEnd)
        {
            var flagsStart = Scanner.Position;
            Scanner.Position = _literal.FlagsEnd;
            flagsToken = Scanner.Token(SyntaxKind.FlagsToken, flagsStart);
            ReportFlagProblems(flagsToken);
        }

        return (closeSlashToken, flagsToken, ReadTrailingContent());
    }

    /// <summary>Keeps whatever followed the flags, which a well-formed literal has none of.</summary>
    private ScannedToken ReadTrailingContent()
    {
        if (Scanner.IsAtEnd)
            return default;

        var start = Scanner.Position;
        Scanner.Position = Text.Length;
        var token = Scanner.Token(SyntaxKind.BadToken, start);
        AddDiagnostic(token.Span, RegexDiagnosticIds.TrailingContent, "Unexpected content after the regular-expression literal.");

        return token;
    }

    /// <summary>Stops the body at the closing delimiter, so the flags are not read as part of the pattern.</summary>
    protected override bool IsAtBodyEnd(int position) =>
        _literal is { HasClosingSlash: true } or { LineTerminatorPosition: >= 0 }
            ? position >= _literal.BodyEnd
            : base.IsAtBodyEnd(position);

    /// <summary>Reports the three ways a flag list can be wrong: unknown, repeated, or <c>u</c> together with <c>v</c>.</summary>
    private void ReportFlagProblems(ScannedToken flagsToken)
    {
        var text = flagsToken.Text;
        var seen = new HashSet<char>();

        for (var index = 0; index < text.Length; index++)
        {
            var flag = text[index];
            var span = new TextSpan(flagsToken.Span.Start + index, 1);

            if (flag is not ('d' or 'g' or 'i' or 'm' or 's' or 'u' or 'v' or 'y'))
            {
                AddDiagnostic(span, RegexDiagnosticIds.UnknownFlag, $"Unknown regular-expression flag '{flag}'.");
                continue;
            }

            if (!seen.Add(flag))
            {
                AddDiagnostic(span, RegexDiagnosticIds.DuplicateFlag, $"The regular-expression flag '{flag}' is repeated.");
            }
        }

        // The two Unicode modes are alternatives, not a pair: "v" is "u" plus the class set grammar.
        if (seen.Contains('u') && seen.Contains('v'))
        {
            AddDiagnostic(flagsToken.Span, RegexDiagnosticIds.ConflictingFlags, "The 'u' and 'v' flags cannot both be set.");
        }
    }
}
