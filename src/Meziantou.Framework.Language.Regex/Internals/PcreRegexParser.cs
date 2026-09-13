using System.Globalization;
using System.Text;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using Red = Meziantou.Framework.Language.Regex;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>Parses a pattern the way PCRE2 does.</summary>
/// <remarks>
/// <para>
/// Most of PCRE is the Perl grammar the shared parser already reads, narrowed or widened by dialect features. What is
/// here is what PCRE has and the others do not: the <c>\g</c> reference family, subroutine calls, callouts, backtracking
/// verbs and alpha assertions, the extra shorthand classes, the braced numeric escapes, and PCRE's own conditions.
/// </para>
/// <para>
/// A pattern is read the way PCRE2 reads it in UTF mode, which is the only mode in which a .NET string is a sequence of
/// characters rather than of bytes.
/// </para>
/// </remarks>
internal sealed class PcreRegexParser : PerlStyleRegexParser
{
    /// <summary>The longest group name PCRE2 accepts, in code units.</summary>
    private const int MaxGroupNameLength = 128;

    /// <summary>The longest a variable-length lookbehind branch may be.</summary>
    private const int MaxLookbehindLength = 255;

    private readonly Dictionary<string, int> _numbersByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _namesByNumber = [];
    private readonly Dictionary<int, (RegexGroupSyntax Group, TextSpan Span)> _captureGroups = [];
    private readonly List<(RegexAlternationSyntax Body, TextSpan Span, int BodyStart)> _lookbehinds = [];

    public PcreRegexParser(SourceText source, RegexParseOptions parseOptions)
        : base(source, parseOptions)
    {
    }

    /// <summary><c>\R</c>, <c>\X</c>, and <c>\C</c> stand for a set of characters but may not appear inside a class.</summary>
    /// <remarks>
    /// <c>\N</c> is "any character except a newline" on its own, but <c>\N{…}</c> names a code point. The brace is
    /// what tells them apart, so the shorthand has to decline when one follows.
    /// </remarks>
    protected override bool IsShorthandClassLetter(char letter) =>
        IsShorthandClassLetterInClass(letter) ||
        letter is 'R' or 'X' or 'C' ||
        (letter == 'N' && (Scanner.Peek(2) != '{' || IsWellFormedBoundAt(Scanner.Position + 2)));

    protected override bool IsShorthandClassLetterInClass(char letter) =>
        base.IsShorthandClassLetterInClass(letter) || letter is 'h' or 'H' or 'v' or 'V';

    protected override bool AllowsEmptyOptionGroup => true;

    protected override bool AllowsShortHexEscape => true;

    protected override bool AllowsAnyControlEscapeCharacter => true;

    protected override bool AllowsBracelessProperty => true;

    protected override bool ReadsCodePoints => true;

    protected override bool SupportsUnicodeEscape => false;

    protected override bool NamedGroupsTakeNumbersInOrder => true;

    protected override bool AllowsNumberedGroups => false;

    protected override bool ReportsShorthandClassAsRangeStart => true;

    protected override long? MaxBoundValue => 65535;

    protected override bool AllowsSpacesInBounds => true;

    protected override bool AllowsOmittedMinimumBound => true;

    private protected override bool ConditionMayStartWithComment => true;

    /// <summary>A name may not start with a digit, which is what tells <c>(?&amp;1)</c> apart from a name.</summary>
    protected override bool IsGroupNameStartAt(int position) =>
        NameCharacterLength(position) > 0 && !char.IsAsciiDigit(Scanner.CharAt(position));

    /// <summary>A name is made of word characters, which in UTF mode include letters outside the Basic Multilingual Plane.</summary>
    protected override string ReadGroupName()
    {
        var start = Scanner.Position;
        while (NameCharacterLength(Scanner.Position) is var length && length > 0)
        {
            Scanner.Position += length;
        }

        return Text[start..Scanner.Position];
    }

    /// <summary>The number of code units of the word character at <paramref name="position"/>, or 0 when there is none.</summary>
    private int NameCharacterLength(int position)
    {
        if (position >= Text.Length || !Rune.TryGetRuneAt(Text, position, out var rune))
            return 0;

        return rune.IsAscii
            ? RegexCharacterTables.IsWordChar((char)rune.Value) ? 1 : 0
            : Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or
                UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation
                ? rune.Utf16SequenceLength
                : 0;
    }

    /// <summary>
    /// A backslash before anything that is not an ASCII letter or digit stands for that character. Inside a class a few
    /// letters that mean something elsewhere are ordinary too.
    /// </summary>
    protected override bool AllowsIdentityEscape(char ch) =>
        !char.IsAsciiLetterOrDigit(ch) || (IsInCharacterClass && ch is '8' or '9' or 'g' or 'k');

    /// <summary>An assertion, a callout, and most verbs match nothing, so there is nothing for a quantifier to repeat.</summary>
    protected override bool IsQuantifiable(RegexTermSyntax term) => term switch
    {
        RegexAnchorSyntax => false,
        RegexCalloutSyntax => false,
        RegexBacktrackingVerbSyntax verb => verb.GetSlot(1) is GreenToken { Text: ['*', 'A', 'C', 'C', 'E', 'P', 'T', ..] },
        RegexCharacterEscapeSyntax escape => escape.GetSlot(0) is not GreenToken { ValueText.Length: 0 },
        RegexQuotedLiteralSyntax quoted => quoted.GetSlot(1) is not null,
        _ => true,
    };

    protected override void CheckGroupNameDeclaration(string name, int number, TextSpan span)
    {
        if (name.Length > MaxGroupNameLength)
        {
            AddDiagnostic(span, RegexDiagnosticIds.CaptureGroupNameInvalid, FormattableString.Invariant($"The group name is longer than {MaxGroupNameLength} characters."));
        }

        // A branch reset group gives the same number to a group in each branch, and those must agree on the name.
        if (_namesByNumber.TryGetValue(number, out var existingName))
        {
            if (existingName != name)
            {
                AddDiagnostic(span, RegexDiagnosticIds.DuplicateGroupName, $"Group {number.ToString(CultureInfo.InvariantCulture)} is already named '{existingName}'.");
            }

            return;
        }

        _namesByNumber[number] = name;
        if (_numbersByName.TryAdd(name, number))
            return;

        if (!DuplicateNamesAllowed)
        {
            AddDiagnostic(span, RegexDiagnosticIds.DuplicateGroupName, $"The group name '{name}' is already used; '(?J)' allows duplicate names.");
        }
    }

    private protected override void OnCaptureGroupParsed(int number, RegexGroupSyntax group) => _captureGroups.TryAdd(number, (group, JustParsedSpan(group)));

    /// <summary>
    /// Reads the option letters PCRE has, including <c>^</c>, which turns off the options a pattern can set before the
    /// letters after it are applied.
    /// </summary>
    private protected override ScannedToken ScanInlineOptions(int start)
    {
        var off = false;
        if (Scanner.Current == '^')
        {
            Options &= ~(RegexPatternOptions.IgnoreCase | RegexPatternOptions.Multiline | RegexPatternOptions.ExplicitCapture | RegexPatternOptions.Singleline | RegexPatternOptions.IgnorePatternWhitespace);
            Scanner.Position++;
            if (Scanner.Current == '-')
            {
                AddDiagnostic(new TextSpan(Scanner.Position, 1), RegexDiagnosticIds.InvalidGroupingConstruct, "A '^' option setting cannot also turn options off.");
                Scanner.Position++;
            }
        }

        while (!Scanner.IsAtEnd)
        {
            var ch = Scanner.Current;
            var option = RegexPatternOptions.None;
            if (ch == '-')
            {
                if (off)
                {
                    AddDiagnostic(new TextSpan(Scanner.Position, 1), RegexDiagnosticIds.InvalidGroupingConstruct, "An option setting may contain only one '-'.");
                }

                off = true;
            }
            else if (ch == 'a' && Scanner.Peek() is 'D' or 'T' or 'P' or 'S')
            {
                // "aD", "aT", "aP", and "aS" restrict one class each to ASCII; the letter after "a" belongs to it.
                Scanner.Position++;
            }
            else if (ch == 'J')
            {
                DuplicateNamesAllowed = !off;
            }
            else if (ch is 'U' or 'r' or 'a' || (char.IsAsciiLetterLower(ch) && TryMapOptionLetter(ch, out option)))
            {
                Options = off ? Options & ~option : Options | option;
            }
            else
            {
                break;
            }

            Scanner.Position++;
        }

        return Scanner.Position > start ? Scanner.Token(SyntaxKind.OptionsToken, start) : default;
    }

    protected override RegexAtomSyntax? TryParseDialectGroupHeader(ScannedToken openParenToken, int questionStart)
    {
        // A subroutine call runs another group's pattern at this point. "(?&name)" is the Perl spelling and
        // "(?P>name)" the Python one; both are recursion by name rather than by number.
        if (Scanner.Current == '&' || (Scanner.Current == 'P' && Scanner.Peek() == '>'))
            return ParseNamedRecursion(openParenToken, questionStart);

        // "(?P=name)" is Python's spelling of a named backreference.
        if (Scanner.Current == 'P' && Scanner.Peek() == '=')
            return ParsePythonNamedBackreference(openParenToken, questionStart);

        if (Scanner.Current == 'C')
            return ParseCallout(openParenToken, questionStart);

        // "(?+" is the start of a relative subroutine call, and needs its digits.
        if (Scanner.Current == '+')
        {
            AddDiagnostic(TextSpan.FromBounds(openParenToken.Span.Start, Scanner.Position + 1), RegexDiagnosticIds.InvalidGroupingConstruct, "A digit is expected after '(?+'.");
        }

        return null;
    }

    protected override RegexAtomSyntax? TryParseDialectEscape(GreenNode? leadingTrivia)
    {
        return Scanner.Peek() switch
        {
            'g' => ParseGReference(leadingTrivia),
            'k' when Scanner.Peek(2) == '{' => ParseBracedNamedBackreference(leadingTrivia),
            '8' or '9' => ParseHighDigitBackreference(leadingTrivia),
            _ => null,
        };
    }

    /// <summary>A property name runs to the closing brace, spaces and all, because PCRE compares names loosely.</summary>
    protected override void ReadPropertyName()
    {
        while (!Scanner.IsAtEnd && Scanner.Current != '}')
        {
            Scanner.Position++;
        }
    }

    /// <summary>
    /// Checks a property name the way PCRE2 looks it up: case, spaces, hyphens, and underscores are ignored, a leading
    /// <c>^</c> negates, and <c>sc:</c>, <c>scx:</c>, and <c>bc:</c> select a script or a bidirectional class.
    /// </summary>
    protected override void ValidatePropertyName(TextSpan span, string name, bool negated, bool braced)
    {
        if (!braced)
        {
            if ((char)(name[0] | 0x20) is not ('c' or 'l' or 'm' or 'n' or 'p' or 's' or 'z'))
            {
                AddDiagnostic(span, RegexDiagnosticIds.UnrecognizedUnicodeProperty, $"Unknown Unicode property '{name}'.");
            }

            return;
        }

        var normalized = string.Concat(name.Where(ch => ch is not (' ' or '-' or '_')).Select(char.ToLowerInvariant));
        if (normalized is ['^', ..])
        {
            normalized = normalized[1..];
        }

        var separator = normalized.IndexOfAny([':', '=']);
        var known = separator < 0
            ? UnicodePropertyNames.PcreProperties.Contains(normalized) || UnicodePropertyNames.PcreScripts.Contains(normalized)
            : normalized[..separator] switch
            {
                "sc" or "script" or "scx" or "scriptextensions" => UnicodePropertyNames.PcreScripts.Contains(normalized[(separator + 1)..]),
                "bc" or "bidiclass" => UnicodePropertyNames.PcreBidiClasses.Contains(normalized[(separator + 1)..]),
                _ => false,
            };

        if (!known)
        {
            AddDiagnostic(span, RegexDiagnosticIds.UnrecognizedUnicodeProperty, $"Unknown Unicode property '{name}'.");
        }
    }

    /// <summary>Parses <c>(*VERB)</c>, <c>(*VERB:NAME)</c>, a start-of-pattern option, or an alpha assertion.</summary>
    private protected override RegexAtomSyntax ParseBacktrackingVerb(ScannedToken openParenToken)
    {
        var verbStart = Scanner.Position;
        Scanner.Position++;
        while (char.IsAsciiLetterOrDigit(Scanner.Current) || Scanner.Current == '_')
        {
            Scanner.Position++;
        }

        var name = Text[(verbStart + 1)..Scanner.Position];
        if (Scanner.Current == ':' && name.Length > 0 && TryGetAlphaAssertionKind(name) is { } kind)
            return ParseAlphaAssertion(openParenToken, verbStart, kind);

        var hasArgument = false;
        if (Scanner.Current == ':')
        {
            hasArgument = Scanner.Peek() != ')';
            while (!Scanner.IsAtEnd && Scanner.Current != ')')
            {
                Scanner.Position++;
            }
        }
        else if (Scanner.Current == '=' && name.StartsWith("LIMIT_", StringComparison.Ordinal))
        {
            Scanner.Position++;
            var digitsStart = Scanner.Position;
            while (char.IsAsciiDigit(Scanner.Current))
            {
                Scanner.Position++;
            }

            hasArgument = Scanner.Position > digitsStart && Scanner.Position - digitsStart <= 10 &&
                long.Parse(Text.AsSpan(digitsStart, Scanner.Position - digitsStart), CultureInfo.InvariantCulture) <= uint.MaxValue;
        }

        var verbToken = Scanner.Token(SyntaxKind.VerbToken, verbStart);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        var verbSpan = TextSpan.FromBounds(openParenToken.Span.Start, Math.Max(openParenToken.Span.Start, Scanner.Position));
        switch (name)
        {
            case "ACCEPT" or "FAIL" or "F" or "COMMIT" or "PRUNE" or "SKIP" or "THEN":
                break;

            case "MARK" or "":
                if (!hasArgument)
                {
                    AddDiagnostic(verbSpan, RegexDiagnosticIds.InvalidBacktrackingVerb, "(*MARK) must have a name.");
                }

                break;

            case var option when IsStartOfPatternOption(option):
                var needsNumber = name.StartsWith("LIMIT_", StringComparison.Ordinal);
                if (needsNumber != hasArgument || (!needsNumber && Scanner.CharAt(verbStart + 1 + name.Length) == ':'))
                {
                    AddDiagnostic(verbSpan, RegexDiagnosticIds.InvalidBacktrackingVerb, $"(*{name}) is malformed.");
                }
                else if (!IsAtStartOfPatternOptions(openParenToken.Span.Start))
                {
                    AddDiagnostic(verbSpan, RegexDiagnosticIds.InvalidBacktrackingVerb, $"(*{name}) is only allowed at the start of the pattern.");
                }

                break;

            default:
                AddDiagnostic(verbSpan, RegexDiagnosticIds.InvalidBacktrackingVerb, $"(*{name}) is not a recognized verb.");
                break;
        }

        return new RegexBacktrackingVerbSyntax(openParenToken, verbToken, closeParenToken, Options);
    }

    /// <summary>Whether only start-of-pattern options such as <c>(*UTF)</c> stand before <paramref name="position"/>.</summary>
    private bool IsAtStartOfPatternOptions(int position)
    {
        var index = 0;
        while (index < position)
        {
            if (Text[index] != '(' || index + 1 >= Text.Length || Text[index + 1] != '*')
                return false;

            var close = Text.IndexOf(')', index, StringComparison.Ordinal);
            if (close < 0 || close >= position)
                return false;

            // Only another option may come first; a verb such as "(*MARK:x)" ends the prefix.
            var name = Text.AsSpan(index + 2, close - index - 2);
            var equals = name.IndexOf('=');
            if (!IsStartOfPatternOption((equals < 0 ? name : name[..equals]).ToString()))
                return false;

            index = close + 1;
        }

        return true;
    }

    private static bool IsStartOfPatternOption(string name) => name is
        "UTF" or "UTF8" or "UCP" or "CR" or "LF" or "CRLF" or "ANYCRLF" or "ANY" or "NUL" or "BSR_ANYCRLF" or "BSR_UNICODE" or
        "NOTEMPTY" or "NOTEMPTY_ATSTART" or "NO_AUTO_POSSESS" or "NO_DOTSTAR_ANCHOR" or "NO_JIT" or "NO_START_OPT" or
        "CASELESS_RESTRICT" or "TURKISH_CASING" or "LIMIT_MATCH" or "LIMIT_DEPTH" or "LIMIT_HEAP" or "LIMIT_RECURSION";

    private static SyntaxKind? TryGetAlphaAssertionKind(string name) => name switch
    {
        "pla" or "positive_lookahead" or "nla" or "negative_lookahead" or "plb" or "positive_lookbehind" or "nlb" or "negative_lookbehind" or
        "napla" or "non_atomic_positive_lookahead" or "naplb" or "non_atomic_positive_lookbehind" => SyntaxKind.Lookaround,
        "atomic" => SyntaxKind.AtomicGroup,
        "sr" or "script_run" or "asr" or "atomic_script_run" => SyntaxKind.NonCapturingGroup,
        _ => null,
    };

    /// <summary>Parses <c>(*pla:…)</c> and the other alpha assertions, which are groups spelled as verbs.</summary>
    private RegexGroupSyntax ParseAlphaAssertion(ScannedToken openParenToken, int kindStart, SyntaxKind kind)
    {
        Scanner.Position++;
        var groupKindToken = Scanner.Token(SyntaxKind.GroupKindToken, kindStart);
        var name = groupKindToken.Text[1..^1];
        var isLookbehind = name is "plb" or "positive_lookbehind" or "nlb" or "negative_lookbehind" or "naplb" or "non_atomic_positive_lookbehind";

        LookaroundDepth += kind == SyntaxKind.Lookaround ? 1 : 0;
        LookbehindDepth += isLookbehind ? 1 : 0;
        var body = ParseAlternation(insideGroup: true);
        LookaroundDepth -= kind == SyntaxKind.Lookaround ? 1 : 0;
        LookbehindDepth -= isLookbehind ? 1 : 0;
        var closeParenToken = ReadCloseParen(openParenToken);

        if (isLookbehind)
        {
            OnLookbehindParsed(body, TextSpan.FromBounds(openParenToken.Span.Start, closeParenToken.End), groupKindToken.End);
        }

        RegexGroupSyntax group = kind switch
        {
            SyntaxKind.Lookaround => new RegexLookaroundSyntax(openParenToken, groupKindToken, body, closeParenToken, Options, body.Options),
            SyntaxKind.AtomicGroup => new RegexAtomicGroupSyntax(openParenToken, groupKindToken, body, closeParenToken, Options, body.Options),
            _ => new RegexNonCapturingGroupSyntax(openParenToken, groupKindToken, body, closeParenToken, Options, body.Options),
        };

        RestoreOptions();

        return group;
    }

    /// <summary>A PCRE conditional names a group, a recursion, <c>DEFINE</c>, or a version, or it is an assertion.</summary>
    private protected override RegexConditionalReferenceSyntax? ReadConditionalReference(int conditionStart)
    {
        var next = Scanner.CharAt(conditionStart + 1);
        if (next is '?' or '*')
        {
            ReportNonAssertionCondition(conditionStart);

            return null;
        }

        Scanner.Position++;
        var openParenToken = Scanner.Token(SyntaxKind.OpenParenToken, conditionStart);

        var nameStart = Scanner.Position;
        while (!Scanner.IsAtEnd && Scanner.Current != ')')
        {
            Scanner.Position++;
        }

        var nameToken = Scanner.Token(SyntaxKind.NameToken, nameStart);
        ValidateConditionName(nameToken);

        ScannedToken closeToken;
        if (Scanner.Current == ')')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            closeToken = Scanner.Token(SyntaxKind.CloseParenToken, closeStart);
        }
        else
        {
            AddDiagnostic(TextSpan.FromBounds(conditionStart, Scanner.Position), RegexDiagnosticIds.InsufficientClosingParentheses, "The condition is missing its ')'.");
            closeToken = Scanner.MissingToken(SyntaxKind.CloseParenToken);
        }

        return new RegexConditionalReferenceSyntax(openParenToken, nameToken, closeToken, Options);
    }

    /// <summary>PCRE has none of the .NET restrictions on an expression condition; its own are checked when it is read.</summary>
    private protected override void ReportIllegalConditionHeader(int conditionStart)
    {
    }

    /// <summary>Reports an expression condition that is not an assertion, optionally preceded by a callout.</summary>
    private void ReportNonAssertionCondition(int conditionStart)
    {
        var position = conditionStart;

        // Comments are allowed in front of the assertion, and so is a callout.
        while (Text.AsSpan(position).StartsWith("(?#", StringComparison.Ordinal) && Text.IndexOf(')', position, StringComparison.Ordinal) is var commentEnd && commentEnd >= 0)
        {
            position = commentEnd + 1;
        }

        if (Text.AsSpan(position).StartsWith("(?C", StringComparison.Ordinal))
        {
            var calloutEnd = Text.IndexOf(')', position, StringComparison.Ordinal);
            if (calloutEnd < 0)
                return;

            position = calloutEnd + 1;
        }

        var rest = Text.AsSpan(position);
        if (rest.StartsWith("(?=", StringComparison.Ordinal) || rest.StartsWith("(?!", StringComparison.Ordinal) ||
            rest.StartsWith("(?<=", StringComparison.Ordinal) || rest.StartsWith("(?<!", StringComparison.Ordinal))
            return;

        if (rest.StartsWith("(*", StringComparison.Ordinal))
        {
            var nameEnd = position + 2;
            while (nameEnd < Text.Length && (char.IsAsciiLetter(Text[nameEnd]) || Text[nameEnd] == '_'))
            {
                nameEnd++;
            }

            if (nameEnd < Text.Length && Text[nameEnd] == ':' && TryGetAlphaAssertionKind(Text[(position + 2)..nameEnd]) == SyntaxKind.Lookaround)
                return;
        }

        AddDiagnostic(new TextSpan(conditionStart, Math.Min(3, Text.Length - conditionStart)), RegexDiagnosticIds.InvalidConditionalCondition, "The condition of a conditional group must be a group reference or an assertion.");
    }

    private void ValidateConditionName(ScannedToken nameToken)
    {
        var name = nameToken.Text;
        var span = nameToken.Span;
        switch (name)
        {
            case "R":
                return;

            case "DEFINE":
                ConditionAllowsOneBranchOnly = true;
                return;

            case ['R', '&', .. var recursedName]:
                ValidateReferencedName(span, recursedName);
                return;

            case ['R', .. var recursedNumber] when recursedNumber.All(char.IsAsciiDigit):
                if (!int.TryParse(recursedNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var recursion) || (recursion != 0 && !CaptureTable.ContainsNumber(recursion)))
                {
                    AddDiagnostic(span, RegexDiagnosticIds.AlternationHasUndefinedReference, $"The condition refers to undefined group {recursedNumber}.");
                }

                return;

            case ['V', 'E', 'R', 'S', 'I', 'O', 'N', .. var version]:
                if (!IsVersionCondition(version))
                {
                    AddDiagnostic(span, RegexDiagnosticIds.AlternationHasMalformedReference, "Malformed '(?(VERSION…' condition.");
                }

                return;

            case ['<', .. var angled, '>']:
                ValidateReferencedName(span, angled);
                return;

            case ['\'', .. var quoted, '\'']:
                ValidateReferencedName(span, quoted);
                return;

            case ['+' or '-', _, ..] when name.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0:
                ReportUnknownRelativeReference(span, int.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var relative) ? relative : int.MaxValue);
                return;

            case [>= '0' and <= '9', ..] when name.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0:
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0 || !CaptureTable.ContainsNumber(number))
                {
                    AddDiagnostic(span, RegexDiagnosticIds.AlternationHasUndefinedReference, $"The condition refers to undefined group {name}.");
                }

                return;

            default:
                ValidateReferencedName(span, name);
                return;
        }
    }

    private void ValidateReferencedName(TextSpan span, string name)
    {
        if (name.Length == 0 || char.IsAsciiDigit(name[0]) || !name.All(RegexCharacterTables.IsWordChar))
        {
            AddDiagnostic(span, RegexDiagnosticIds.AlternationHasMalformedReference, $"'{name}' is not a valid group name.");
        }
        else if (!CaptureTable.TryGetNumber(name, out _))
        {
            AddDiagnostic(span, RegexDiagnosticIds.AlternationHasUndefinedReference, $"The condition refers to undefined group '{name}'.");
        }
    }

    /// <summary>Whether the text after <c>VERSION</c> is <c>=</c> or <c>&gt;=</c> and a version number.</summary>
    private static bool IsVersionCondition(string text)
    {
        var span = text.AsSpan();
        if (span.StartsWith(">=", StringComparison.Ordinal))
        {
            span = span[2..];
        }
        else if (span.StartsWith("=", StringComparison.Ordinal))
        {
            span = span[1..];
        }
        else
        {
            return false;
        }

        var dot = span.IndexOf('.');
        var major = dot < 0 ? span : span[..dot];
        if (major.Length == 0 || major.IndexOfAnyExceptInRange('0', '9') >= 0)
            return false;

        if (dot < 0)
            return true;

        var minor = span[(dot + 1)..];

        return minor.Length > 0 && minor.IndexOfAnyExceptInRange('0', '9') < 0;
    }

    /// <summary>
    /// Parses the <c>\g</c> family: <c>\g1</c>, <c>\g{1}</c>, <c>\g{-1}</c>, and <c>\g{name}</c> are backreferences,
    /// while <c>\g&lt;name&gt;</c> and <c>\g'name'</c> are subroutine calls.
    /// </summary>
    private RegexAtomSyntax ParseGReference(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var startToken = Scanner.Token(SyntaxKind.NamedBackreferenceStartToken, start, leadingTrivia);

        // The angled and quoted spellings call a group rather than referring back to what it matched.
        if (Scanner.Current is '<' or '\'')
        {
            var close = Scanner.Current == '\'' ? '\'' : '>';
            var openStart = Scanner.Position;
            Scanner.Position++;
            var openToken = Scanner.Token(SyntaxKind.OpenNameToken, openStart);
            var target = ReadUntil(close, SyntaxKind.RecursionToken);
            ReportUnknownRecursionTarget(target);
            var closeToken = ReadExpected(close, SyntaxKind.CloseNameToken, startToken);
            if (!closeToken.IsPresent)
            {
                closeToken = Scanner.MissingToken(SyntaxKind.CloseNameToken);
            }

            return new RegexRecursionSyntax(startToken, openToken, target, closeToken, Options);
        }

        if (Scanner.Current == '{')
        {
            var openStart = Scanner.Position;
            Scanner.Position++;
            var openToken = Scanner.Token(SyntaxKind.OpenNameToken, openStart);
            var nameStart = Scanner.Position;
            var name = ReadUntil('}', SyntaxKind.NameToken);
            var closeToken = ReadExpected('}', SyntaxKind.CloseNameToken, startToken);
            ReportUnknownReference(name, nameStart);

            return new RegexNamedBackreferenceSyntax(startToken, openToken, name, closeToken, Options);
        }

        // "\g1" and "\g-1": a bare number, possibly relative.
        var numberStart = Scanner.Position;
        if (Scanner.Current is '-' or '+')
        {
            Scanner.Position++;
        }

        while (char.IsAsciiDigit(Scanner.Current))
        {
            Scanner.Position++;
        }

        if (Scanner.Position == numberStart || !char.IsAsciiDigit(Scanner.CharAt(Scanner.Position - 1)))
        {
            AddDiagnostic(startToken.Span, RegexDiagnosticIds.MalformedNamedReference, "Malformed '\\g' reference.");
            Scanner.Position = numberStart;

            return new RegexNamedBackreferenceSyntax(startToken, null, null, null, Options);
        }

        var numberToken = Scanner.Token(SyntaxKind.NameToken, numberStart);
        ReportUnknownReference(numberToken, numberStart);

        return new RegexNamedBackreferenceSyntax(startToken, null, numberToken, null, Options);
    }

    /// <summary>Parses <c>\k{name}</c>, the Perl spelling of a named backreference.</summary>
    private RegexNamedBackreferenceSyntax ParseBracedNamedBackreference(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var startToken = Scanner.Token(SyntaxKind.NamedBackreferenceStartToken, start, leadingTrivia);
        var openStart = Scanner.Position;
        Scanner.Position++;
        var openToken = Scanner.Token(SyntaxKind.OpenNameToken, openStart);
        var nameStart = Scanner.Position;
        var name = ReadUntil('}', SyntaxKind.NameToken);
        var closeToken = ReadExpected('}', SyntaxKind.CloseNameToken, startToken);
        if (name.IsPresent)
        {
            ReportUnknownReference(name, nameStart, trimSpaces: true, numbersAllowed: false);
        }
        else
        {
            AddDiagnostic(startToken.Span, RegexDiagnosticIds.MalformedNamedReference, "'\\k{' must be followed by a group name.");
        }

        return new RegexNamedBackreferenceSyntax(startToken, openToken, name, closeToken, Options);
    }

    /// <summary>Parses <c>\8</c> and <c>\9</c>, which PCRE always reads as backreferences, whatever digits follow.</summary>
    private RegexBackreferenceSyntax ParseHighDigitBackreference(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position++;
        long number = 0;
        while (char.IsAsciiDigit(Scanner.Current))
        {
            number = Math.Min((number * 10) + (Scanner.Current - '0'), int.MaxValue);
            Scanner.Position++;
        }

        var token = Scanner.Token(SyntaxKind.BackreferenceToken, start, leadingTrivia, ((int)number).ToString(CultureInfo.InvariantCulture));
        if (!CaptureTable.ContainsNumber((int)number))
        {
            AddDiagnostic(token.Span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {token.Text[1..]}.");
        }

        return new RegexBackreferenceSyntax(token, Options);
    }

    /// <summary>Parses <c>(?&amp;name)</c> and <c>(?P&gt;name)</c>.</summary>
    private RegexRecursionSyntax ParseNamedRecursion(ScannedToken openParenToken, int questionStart)
    {
        Scanner.Position += Scanner.Current == '&' ? 1 : 2;
        var questionToken = Scanner.Token(SyntaxKind.QuestionToken, questionStart);

        var target = ReadGroupNameToken(SyntaxKind.RecursionToken);
        ReportUnknownRecursionTarget(target);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return new RegexRecursionSyntax(openParenToken, questionToken, target, closeParenToken, Options);
    }

    /// <summary>Parses <c>(?P=name)</c>.</summary>
    private RegexNamedBackreferenceSyntax ParsePythonNamedBackreference(ScannedToken openParenToken, int questionStart)
    {
        Scanner.Position += 2;
        var markerToken = Scanner.Token(SyntaxKind.NamedBackreferenceStartToken, questionStart);

        var nameStart = Scanner.Position;
        var name = ReadGroupNameToken(SyntaxKind.NameToken);
        var closeToken = ReadCloseParen(openParenToken);
        RestoreOptions();
        ReportUnknownReference(name, nameStart, trimSpaces: false, numbersAllowed: false);

        return new RegexNamedBackreferenceSyntax(openParenToken, markerToken, name, closeToken, Options);
    }

    /// <summary>Reads a group name, reporting one that is missing or starts with a digit.</summary>
    private ScannedToken ReadGroupNameToken(SyntaxKind kind)
    {
        var start = Scanner.Position;
        if (char.IsAsciiDigit(Scanner.Current))
        {
            AddDiagnostic(new TextSpan(start, 1), RegexDiagnosticIds.CaptureGroupNameInvalid, "A group name must not start with a digit.");
        }

        while (!Scanner.IsAtEnd && RegexCharacterTables.IsWordChar(Scanner.Current))
        {
            Scanner.Position++;
        }

        if (Scanner.Position == start)
        {
            AddDiagnostic(new TextSpan(start, 0), RegexDiagnosticIds.CaptureGroupNameInvalid, "A group name is expected.");

            return default;
        }

        return Scanner.Token(kind, start);
    }

    /// <summary>Parses <c>(?C)</c>, <c>(?C1)</c>, and <c>(?C"text")</c>.</summary>
    private RegexCalloutSyntax ParseCallout(ScannedToken openParenToken, int questionStart)
    {
        Scanner.Position++;
        var questionToken = Scanner.Token(SyntaxKind.QuestionToken, questionStart);

        var bodyStart = Scanner.Position;
        if (char.IsAsciiDigit(Scanner.Current))
        {
            long number = 0;
            while (char.IsAsciiDigit(Scanner.Current))
            {
                number = Math.Min((number * 10) + (Scanner.Current - '0'), int.MaxValue);
                Scanner.Position++;
            }

            if (number > 255)
            {
                AddDiagnostic(TextSpan.FromBounds(bodyStart, Scanner.Position), RegexDiagnosticIds.InvalidCallout, "A callout number cannot be greater than 255.");
            }
        }
        else if (Scanner.Current is '`' or '\'' or '"' or '^' or '%' or '#' or '$' or '{')
        {
            // A delimiter is closed by itself, or "{" by "}", and a doubled closing delimiter stands for one.
            var close = Scanner.Current == '{' ? '}' : Scanner.Current;
            Scanner.Position++;
            while (true)
            {
                if (Scanner.IsAtEnd)
                {
                    AddDiagnostic(TextSpan.FromBounds(bodyStart, Scanner.Position), RegexDiagnosticIds.InvalidCallout, "The callout string is missing its closing delimiter.");
                    break;
                }

                if (Scanner.Current == close)
                {
                    Scanner.Position++;
                    if (Scanner.Current != close)
                        break;
                }

                Scanner.Position++;
            }
        }
        else if (Scanner.Current != ')')
        {
            AddDiagnostic(new TextSpan(Scanner.Position, Scanner.IsAtEnd ? 0 : 1), RegexDiagnosticIds.InvalidCallout, "'(?C' must be followed by a number, a delimited string, or ')'.");
        }

        if (Scanner.Current != ')' && !Scanner.IsAtEnd)
        {
            AddDiagnostic(new TextSpan(Scanner.Position, 1), RegexDiagnosticIds.InvalidCallout, "A callout must be closed by ')'.");
            while (!Scanner.IsAtEnd && Scanner.Current != ')')
            {
                Scanner.Position++;
            }
        }

        var body = Scanner.Position > bodyStart ? Scanner.Token(SyntaxKind.CalloutToken, bodyStart) : default;
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return new RegexCalloutSyntax(openParenToken, questionToken, body, closeParenToken, Options);
    }

    /// <summary>Reads <c>\o{101}</c>, <c>\x{41}</c>, and <c>\N{U+0041}</c>, all of which name a code point in braces.</summary>
    private protected override string? TryScanDialectCharacterEscape(char letter, int escapeStart)
    {
        if (letter is not ('o' or 'x' or 'N') || Scanner.Current != '{')
            return null;

        return ScanBracedNumericEscape(escapeStart, octal: letter == 'o', hexOnly: letter == 'x');
    }

    private string ScanBracedNumericEscape(int start, bool octal, bool hexOnly)
    {
        Scanner.Position++;

        var digitsStart = Scanner.Position;
        while (!Scanner.IsAtEnd && Scanner.Current != '}')
        {
            Scanner.Position++;
        }

        // "\x{…}" and "\o{…}" may pad their digits with spaces; "\N{U+…}" may not.
        var digits = Text[digitsStart..Scanner.Position];
        if (octal || hexOnly)
        {
            digits = digits.Trim(' ');
        }

        var closed = Scanner.Current == '}';
        if (closed)
        {
            Scanner.Position++;
        }

        var value = TryReadCodePoint(hexOnly ? "U+" + digits : digits, octal, out var codePoint) && closed
            ? char.ConvertFromUtf32(codePoint)
            : string.Empty;

        if (value.Length == 0)
        {
            AddDiagnostic(
                TextSpan.FromBounds(start, Scanner.Position),
                RegexDiagnosticIds.InsufficientOrInvalidHexDigits,
                octal ? "The '\\o{...}' escape is not a well-formed octal value." : "The braced escape is not a well-formed code point.");
        }

        return value;
    }

    private static bool TryReadCodePoint(string digits, bool octal, out int codePoint)
    {
        codePoint = 0;
        var span = digits.AsSpan();
        if (!octal)
        {
            // "\N{U+xxxx}" names a code point in hexadecimal; the "U+" is part of the syntax.
            if (!span.StartsWith("U+", StringComparison.Ordinal))
                return false;

            span = span[2..];
        }

        if (span.Length == 0)
            return false;

        var radix = octal ? 8 : 16;
        foreach (var ch in span)
        {
            var digit = octal
                ? (ch is >= '0' and <= '7' ? ch - '0' : -1)
                : Uri.IsHexDigit(ch) ? Uri.FromHex(ch) : -1;

            if (digit < 0)
                return false;

            codePoint = (codePoint * radix) + digit;
            if (codePoint > 0x10FFFF)
                return false;
        }

        // Surrogates are not scalar values, so they cannot be written as a code point.
        return codePoint is < 0xD800 or > 0xDFFF;
    }

    /// <summary>Reads everything up to <paramref name="terminator"/>, without consuming it.</summary>
    private ScannedToken ReadUntil(char terminator, SyntaxKind kind)
    {
        var start = Scanner.Position;
        while (!Scanner.IsAtEnd && Scanner.Current != terminator && Scanner.Current != ')')
        {
            Scanner.Position++;
        }

        return Scanner.Position > start ? Scanner.Token(kind, start) : default;
    }

    private ScannedToken ReadExpected(char expected, SyntaxKind kind, ScannedToken owner)
    {
        if (Scanner.Current != expected)
        {
            AddDiagnostic(owner.Span, RegexDiagnosticIds.MalformedNamedReference, $"Expected '{expected}' to close the reference.");

            return default;
        }

        var start = Scanner.Position;
        Scanner.Position++;

        return Scanner.Token(kind, start);
    }

    /// <summary>Reports a reference that names neither an existing group number nor an existing group name.</summary>
    private void ReportUnknownReference(ScannedToken nameToken, int nameStart, bool trimSpaces = true, bool numbersAllowed = true)
    {
        if (!nameToken.IsPresent)
            return;

        var name = trimSpaces ? nameToken.Text.Trim(' ') : nameToken.Text;
        var span = new TextSpan(nameStart, nameToken.Text.Length);

        if (numbersAllowed && name is ['-' or '+', _, ..] && name.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0)
        {
            ReportUnknownRelativeReference(span, int.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var relative) ? relative : int.MaxValue);

            return;
        }

        if (name.Length > 0 && char.IsAsciiDigit(name[0]))
        {
            if (!numbersAllowed || name.AsSpan().IndexOfAnyExceptInRange('0', '9') >= 0)
            {
                AddDiagnostic(span, RegexDiagnosticIds.MalformedNamedReference, $"'{name}' is not a valid group name or number.");
            }
            else if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || !CaptureTable.ContainsNumber(number))
            {
                AddDiagnostic(span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {name}.");
            }

            return;
        }

        if (name.Length == 0 || !name.All(RegexCharacterTables.IsWordChar))
        {
            AddDiagnostic(span, RegexDiagnosticIds.MalformedNamedReference, $"'{name}' is not a valid group name.");
        }
        else if (!CaptureTable.TryGetNumber(name, out _))
        {
            AddDiagnostic(span, RegexDiagnosticIds.UndefinedNamedReference, $"Reference to undefined group name '{name}'.");
        }
    }

    // ---- lookbehind length ----

    private protected override void OnLookbehindParsed(RegexAlternationSyntax body, TextSpan span, int bodyStart) => _lookbehinds.Add((body, span, bodyStart));

    /// <summary>
    /// Checks every lookbehind once the whole pattern is known, since a subroutine call inside one may run a group
    /// declared after it.
    /// </summary>
    /// <remarks>
    /// Each top-level branch must have a bounded length, and a branch whose length varies may not be longer than 255
    /// characters. A branch of a fixed length may be as long as it likes.
    /// </remarks>
    private protected override void OnPatternParsed()
    {
        foreach (var (body, span, bodyStart) in _lookbehinds)
        {
            var red = (Red.RegexAlternationSyntax)body.CreateRed();
            foreach (var branch in red.Branches)
            {
                var (min, max) = MeasureLength(branch, bodyStart, []);
                if (max is null)
                {
                    AddDiagnostic(span, RegexDiagnosticIds.UnboundedLookbehind, "The length of a lookbehind assertion must be limited.");
                    break;
                }

                if (min != max && max > MaxLookbehindLength)
                {
                    AddDiagnostic(span, RegexDiagnosticIds.UnboundedLookbehind, FormattableString.Invariant($"A variable-length lookbehind branch cannot be longer than {MaxLookbehindLength} characters."));
                    break;
                }
            }

            foreach (var escape in red.DescendantNodes().OfType<Red.RegexCharacterClassEscapeSyntax>())
            {
                if (escape.EscapeToken.Text == "\\C")
                {
                    AddDiagnostic(span, RegexDiagnosticIds.UnboundedLookbehind, "'\\C' is not allowed in a lookbehind assertion.");
                    break;
                }
            }
        }
    }

    /// <summary>The fewest and most characters <paramref name="node"/> can match, the most being null when unbounded.</summary>
    private (long Min, long? Max) MeasureLength(SyntaxNode node, int offset, HashSet<int> calling)
    {
        switch (node)
        {
            case Red.RegexAlternationSyntax alternation:
            {
                long? min = null;
                long? max = 0;
                foreach (var branch in alternation.Branches)
                {
                    var (branchMin, branchMax) = MeasureLength(branch, offset, calling);
                    min = min is null ? branchMin : Math.Min(min.Value, branchMin);
                    max = max is null || branchMax is null ? null : Math.Max(max.Value, branchMax.Value);
                }

                return (min ?? 0, max);
            }

            case Red.RegexSequenceSyntax sequence:
            {
                long min = 0;
                long? max = 0;
                foreach (var term in sequence.Terms)
                {
                    var (termMin, termMax) = MeasureLength(term, offset, calling);
                    min = Math.Min(min + termMin, int.MaxValue);
                    max = max is null || termMax is null ? null : Math.Min(max.Value + termMax.Value, int.MaxValue);
                }

                return (min, max);
            }

            case Red.RegexQuantifiedSyntax quantified:
            {
                // An assertion matches nothing however often it is repeated. Anything else repeated without limit is
                // unbounded, even when it happens to match nothing.
                if (quantified.Term is Red.RegexLookaroundSyntax or Red.RegexAnchorSyntax)
                    return (0, 0);

                var (termMin, termMax) = MeasureLength(quantified.Term, offset, calling);
                var min = Math.Min(termMin * quantified.Quantifier.MinCount, int.MaxValue);
                long? max = quantified.Quantifier.MaxCount is { } count && termMax is not null
                    ? Math.Min(termMax.Value * count, int.MaxValue)
                    : null;

                return (min, max);
            }

            case Red.RegexLookaroundSyntax or Red.RegexAnchorSyntax or Red.RegexInlineOptionsSyntax or Red.RegexCalloutSyntax or Red.RegexBacktrackingVerbSyntax:
                return (0, 0);

            case Red.RegexConditionalSyntax conditional:
            {
                var (min, max) = MeasureLength(conditional.Alternation, offset, calling);

                return (conditional.Alternation.Branches.Count < 2 ? 0 : min, max);
            }

            case Red.RegexGroupSyntax group:
                return group.ChildNodes().OfType<Red.RegexAlternationSyntax>().FirstOrDefault() is { } body ? MeasureLength(body, offset, calling) : (0, 0);

            case Red.RegexCharacterClassEscapeSyntax escape:
                return escape.EscapeToken.Text switch
                {
                    "\\X" => (1, null),
                    "\\R" => (1, 2),
                    _ => (1, 1),
                };

            case Red.RegexCharacterEscapeSyntax escape when escape.Value.Length == 0:
                return (0, 0);

            case Red.RegexQuotedLiteralSyntax quoted:
            {
                var length = quoted.Value.EnumerateRunes().Count();

                return (length, length);
            }

            case Red.RegexBackreferenceSyntax backreference:
                return MeasureGroup(backreference.Number, offset + backreference.SpanStart, calling);

            case Red.RegexNamedBackreferenceSyntax namedBackreference:
                return MeasureGroup(ResolveReference(namedBackreference.Name, offset + namedBackreference.SpanStart), offset + namedBackreference.SpanStart, calling);

            case Red.RegexRecursionSyntax recursion:
                return recursion.TargetToken.Text is "R" or "0" ? (0, null) : MeasureGroup(ResolveReference(recursion.TargetToken.Text, offset + recursion.SpanStart), offset + recursion.SpanStart, calling);

            default:
                return (1, 1);
        }
    }

    /// <summary>Measures the group a reference or a call names, treating a call into itself as unbounded.</summary>
    private (long Min, long? Max) MeasureGroup(int number, int referencePosition, HashSet<int> calling)
    {
        if (!_captureGroups.TryGetValue(number, out var group))
            return (0, 0);

        // A reference from inside the group it names depends on its own length, which is never fixed.
        if (group.Span.Contains(referencePosition) || !calling.Add(number))
            return (0, null);

        var length = MeasureLength(group.Group.CreateRed(), group.Span.Start, calling);
        calling.Remove(number);

        return length;
    }

    /// <summary>The group number a reference names, relative ones included.</summary>
    private int ResolveReference(string reference, int position)
    {
        if (reference is ['-' or '+', _, ..] && int.TryParse(reference, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var relative))
        {
            var opened = 0;
            foreach (var number in CaptureTable.Numbers)
            {
                if (CaptureTable.GetPosition(number) < position)
                {
                    opened = number;
                }
            }

            return relative < 0 ? opened + relative + 1 : opened + relative;
        }

        if (int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var absolute))
            return absolute;

        return CaptureTable.TryGetNumber(reference, out var named) ? named : 0;
    }
}
