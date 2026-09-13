// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
//
// Changes: ScanCharClass builds member nodes instead of a RegexCharClass, and the explicit stack of parent classes
// becomes recursion bounded by RegexParseOptions.MaxRecursionDepth. Which characters it consumes is unchanged for .NET.
// The rules of the other dialects -- POSIX bracket expressions, PCRE's quoting, the ECMAScript class set grammar -- are
// additions.

using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using ScannedToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

internal abstract partial class PerlStyleRegexParser
{
    /// <summary>The characters a class set has to have escaped, because unescaped they mean something else.</summary>
    private const string ClassSetSyntaxCharacters = "()[]{}/-|";

    /// <summary>
    /// The characters that may not appear doubled. The grammar reserves them so the syntax can grow later without
    /// changing what an existing pattern means.
    /// </summary>
    /// <remarks><c>&amp;</c> and <c>-</c> are not here: doubled they are the operators, which are read before this.</remarks>
    private const string ReservedDoublePunctuators = "!#$%*+,.:;<=>?@^`~";

    /// <summary>Whether the last class set member read may match a string of other than one character.</summary>
    private protected bool LastSetMemberMayContainStrings { get; set; }

    /// <summary>Whether a backslash inside a bracket expression is an ordinary character, as it is in POSIX.</summary>
    protected virtual bool BackslashIsLiteralInClass => false;

    /// <summary>
    /// Whether a dash right after a completed range must end the class. POSIX rejects <c>[a-z-9]</c>; the Perl
    /// dialects read that dash as an ordinary character.
    /// </summary>
    protected virtual bool RejectsDashAfterRange => false;

    /// <summary>Parses a character class, guarding against input that nests subtractions without end.</summary>
    private RegexAtomSyntax ParseCharacterClass(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        if (!TryEnterRecursion(new TextSpan(start, 1)))
            return ConsumeRestAsText(start, leadingTrivia);

        try
        {
            Scanner.Position++;

            return ParseCharacterClassBody(Scanner.Token(SyntaxKind.OpenBracketToken, start, leadingTrivia));
        }
        finally
        {
            ExitRecursion();
        }
    }

    /// <summary>
    /// Reads the members of a class, from just after the <c>[</c> through the matching <c>]</c>.
    /// </summary>
    /// <remarks>
    /// Whitespace and <c>#</c> are ordinary characters in here even in extended mode, because the engine never scans
    /// trivia from inside a class. Nothing below may call the trivia scanner.
    /// </remarks>
    private RegexCharacterClassSyntax ParseCharacterClassBody(ScannedToken openBracketToken)
    {
        // A few escapes are allowed inside a class and nowhere else, so the reader has to know where it is.
        var wasInCharacterClass = IsInCharacterClass;
        IsInCharacterClass = true;
        try
        {
            return UsesUnicodeSetsMode ? ParseClassSetMembers(openBracketToken) : ParseCharacterClassMembers(openBracketToken);
        }
        finally
        {
            IsInCharacterClass = wasInCharacterClass;
        }
    }

    private RegexCharacterClassSyntax ParseCharacterClassMembers(ScannedToken openBracketToken)
    {
        ScannedToken caretToken = default;
        var firstChar = true;

        if (Scanner.Current == '^')
        {
            var caretStart = Scanner.Position;
            Scanner.Position++;
            caretToken = Scanner.Token(SyntaxKind.CaretToken, caretStart);

            // Under ECMAScript "[^]" is an empty negated class rather than a class containing "]".
            if (UsesEcmaScriptBehavior && Scanner.Current == ']')
            {
                firstChar = false;
            }
        }
        else if (AllowsEmptyCharacterClass && Scanner.Current == ']')
        {
            // "[]" is an empty class that matches nothing. Only ECMAScript has it: .NET reads the "]" as a member and
            // then runs out of pattern looking for the real one.
            firstChar = false;
        }

        ReportPosixSyntaxOutsideClass(openBracketToken.Span.Start);

        var members = new List<RegexSyntaxNode>();
        RegexSyntaxNode? rangeStart = null;
        ScannedToken rangeHyphen = default;
        var rangeStartValue = 0;
        var inRange = false;
        var afterRange = false;
        var noRangeStart = false;
        ScannedToken closeBracketToken = default;

        while (!Scanner.IsAtEnd)
        {
            if (Scanner.Current == ']' && !firstChar)
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                closeBracketToken = Scanner.Token(SyntaxKind.CloseBracketToken, closeStart);
                break;
            }

            // A subtraction reached through a range dash, as in "[a-[b]]": the dash was already claimed by the
            // look-ahead that started the range, so the character it would have ranged to is a plain member instead.
            if (!firstChar && inRange && Scanner.Current == '[' && Dialect.HasFeature(RegexDialectFeatures.CharacterClassSubtraction))
            {
                inRange = false;
                members.Add(rangeStart!);
                members.Add(ParseSubtraction(rangeHyphen!));
                firstChar = false;
                continue;
            }

            // A subtraction with a dash of its own, as in "[a-z-[b]]".
            if (!firstChar && !inRange && Scanner.Current == '-' && Scanner.Peek() == '[' && Dialect.HasFeature(RegexDialectFeatures.CharacterClassSubtraction))
            {
                var hyphenStart = Scanner.Position;
                Scanner.Position++;
                members.Add(ParseSubtraction(Scanner.Token(SyntaxKind.HyphenToken, hyphenStart)));
                firstChar = false;
                continue;
            }

            // "\Q…\E" quotes inside a class too, and a "\E" with nothing to close is simply ignored.
            if (Dialect.HasFeature(RegexDialectFeatures.QuotedLiterals) && !inRange && Scanner.Current == '\\' && Scanner.Peek() is 'Q' or 'E')
            {
                var isEmpty = Scanner.Peek() == 'E' || Text.AsSpan(Scanner.Position + 2).StartsWith("\\E", StringComparison.Ordinal);
                members.Add(Scanner.Peek() == 'Q' ? ParseQuotedLiteral(leadingTrivia: null) : ParseStrayQuoteEnd());

                // An empty quote contributes nothing, so a "]" after it is still the first character of the class.
                firstChar = firstChar && isEmpty;
                continue;
            }

            if (Dialect.HasFeature(RegexDialectFeatures.PosixBracketExpressions) && TryParsePosixBracket(out var bracket, out var isCollatingSymbol))
            {
                afterRange = inRange;
                if (inRange)
                {
                    // A collating symbol stands for one character, so it may end a range. A class or an equivalence
                    // class stands for many and may not.
                    if (isCollatingSymbol && Dialect.Family == RegexDialectFamily.Posix)
                    {
                        var range = new RegexCharacterRangeSyntax(rangeStart!, rangeHyphen!, bracket, Options);
                        if (rangeStartValue > CollatingSymbolValue(bracket))
                        {
                            AddDiagnostic(JustParsedSpan(range), RegexDiagnosticIds.ReversedCharacterRange, $"Character range '{range}' is reversed.");
                        }

                        members.Add(range);
                    }
                    else
                    {
                        var range = new RegexCharacterRangeSyntax(rangeStart!, rangeHyphen!, bracket, Options);
                        AddDiagnostic(JustParsedSpan(range), RegexDiagnosticIds.ShorthandClassInCharacterRange, $"'{bracket}' cannot be an endpoint of a character range.");
                        members.Add(range);
                    }

                    inRange = false;
                }
                else if (isCollatingSymbol && Dialect.Family == RegexDialectFamily.Posix && TryStartRange(bracket, CollatingSymbolValue(bracket)))
                {
                    rangeStart = bracket;
                }
                else
                {
                    ReportClassAsRangeStart(bracket);
                    members.Add(bracket);
                }

                firstChar = false;
                continue;
            }

            if (afterRange && RejectsDashAfterRange && Scanner.Current == '-' && Scanner.Peek() != ']')
            {
                AddDiagnostic(new TextSpan(Scanner.Position, 1), RegexDiagnosticIds.ReversedCharacterRange, "A range cannot be followed by '-' unless it ends the bracket expression.");
            }

            afterRange = false;
            var element = ReadClassElement();

            if (element.IsClassEscape)
            {
                if (inRange && AllowsShorthandClassInRange)
                {
                    // Where this is allowed there is no range at all: the dash between them is an ordinary member.
                    members.Add(rangeStart!);
                    members.Add(new RegexLiteralSyntax(
                        SyntaxFactory.Token(SyntaxKind.LiteralToken, rangeHyphen.Text, rangeHyphen.Green!.LeadingTrivia), Options));
                    members.Add(element.Node);
                    inRange = false;
                }
                else if (inRange)
                {
                    // The engine rejects a shorthand class as a range endpoint outright. Keeping the range in the tree
                    // is the recovery: it accounts for every character, and the diagnostic says what is wrong with it.
                    AddDiagnostic(JustParsedSpan(element.Node), RegexDiagnosticIds.ShorthandClassInCharacterRange, $"Shorthand class '{element.Node}' cannot be an endpoint of a character range.");
                    members.Add(new RegexCharacterRangeSyntax(rangeStart!, rangeHyphen!, element.Node, Options));
                    inRange = false;
                }
                else
                {
                    ReportClassAsRangeStart(element.Node);
                    members.Add(element.Node);

                    // Where a shorthand may be an endpoint, "[\d-x]" is three members, and the "x" after the dash is
                    // an endpoint too, so it cannot start a range of its own.
                    if (AllowsShorthandClassInRange && Scanner.Current == '-' && Scanner.Position + 1 < Text.Length && Scanner.Peek() != ']')
                    {
                        var dashStart = Scanner.Position;
                        Scanner.Position++;
                        members.Add(new RegexLiteralSyntax(Scanner.Token(SyntaxKind.LiteralToken, dashStart), Options));
                        noRangeStart = true;
                    }
                }

                firstChar = false;
                continue;
            }

            // In .NET "\-" completes a range or stands for a literal dash, but never starts a range: the engine handles
            // it before the look-ahead that a plain character would go through. Elsewhere it is a dash like any other.
            if (element.IsDashEscape && Dialect.Family == RegexDialectFamily.Dotnet)
            {
                if (inRange)
                {
                    AddRange(members, rangeStart!, rangeHyphen!, element, rangeStartValue);
                    inRange = false;
                }
                else
                {
                    members.Add(element.Node);
                }

                firstChar = false;
                continue;
            }

            if (inRange)
            {
                AddRange(members, rangeStart!, rangeHyphen!, element, rangeStartValue);
                inRange = false;
                afterRange = true;
            }
            else if (noRangeStart || !TryStartRange(element.Node, element.Value))
            {
                members.Add(element.Node);
            }

            noRangeStart = false;
            firstChar = false;
        }

        if (inRange)
        {
            // Unreachable while the look-ahead that starts a range demands a character after the dash, but a class that
            // lost its endpoint must still account for the two members it does have.
            members.Add(rangeStart!);
            members.Add(new RegexCharacterRangeSyntax(rangeStart!, rangeHyphen, end: null, Options));
        }

        if (!closeBracketToken.IsPresent)
        {
            AddDiagnostic(
                TextSpan.FromBounds(openBracketToken.Span.Start, Math.Max(openBracketToken.Span.Start, Scanner.Position)),
                RegexDiagnosticIds.UnterminatedBracket,
                "Unterminated character class: expected ']'.");
            closeBracketToken = Scanner.MissingToken(SyntaxKind.CloseBracketToken);
        }

        return new RegexCharacterClassSyntax(openBracketToken, caretToken, SyntaxFactory.ListNode([.. members]), closeBracketToken, Options);

        // A dash that is followed by something other than the closing bracket starts a range from the member before it.
        bool TryStartRange(RegexSyntaxNode node, int value)
        {
            if (Scanner.Position + 1 >= Text.Length || Text[Scanner.Position] != '-' || Text[Scanner.Position + 1] == ']')
                return false;

            var hyphenStart = Scanner.Position;
            Scanner.Position++;
            rangeStart = node;
            rangeStartValue = value;
            rangeHyphen = Scanner.Token(SyntaxKind.HyphenToken, hyphenStart);
            inRange = true;

            return true;
        }
    }

    /// <summary>
    /// Reports a class, a shorthand or a POSIX one, followed by a dash that would make it the start of a range. The
    /// dash is left where it is, so the rest of the class is read as if the range were not there.
    /// </summary>
    private void ReportClassAsRangeStart(RegexSyntaxNode member)
    {
        if (!ReportsShorthandClassAsRangeStart || Scanner.Current != '-' || Scanner.Peek() is ']' or '\0' || ClassSetOperatorLength(Scanner.Position) > 0)
            return;

        AddDiagnostic(
            JustParsedSpan(member),
            RegexDiagnosticIds.ShorthandClassInCharacterRange,
            $"'{member}' cannot be the start of a character range.");
    }

    /// <summary>Keeps a <c>\E</c> that closes no quote, which the engines ignore.</summary>
    private RegexCharacterEscapeSyntax ParseStrayQuoteEnd()
    {
        var start = Scanner.Position;
        Scanner.Position += 2;

        return new RegexCharacterEscapeSyntax(Scanner.Token(SyntaxKind.EscapeToken, start, leadingTrivia: null, string.Empty), Options);
    }

    /// <summary>
    /// Reports <c>[:alpha:]</c> written without the brackets around it, which PCRE rejects rather than reading as a
    /// class of five characters.
    /// </summary>
    private void ReportPosixSyntaxOutsideClass(int openBracket)
    {
        if (Dialect.Family != RegexDialectFamily.Pcre || Scanner.Position != openBracket + 1 || Scanner.Current is not (':' or '.' or '='))
            return;

        if (FindPcrePosixTerminator(openBracket, Scanner.Current) is var terminator && terminator >= 0)
        {
            AddDiagnostic(
                TextSpan.FromBounds(openBracket, terminator + 2),
                RegexDiagnosticIds.InvalidPosixBracketExpression,
                Scanner.Current == ':' ? "A POSIX named class is only allowed inside a character class." : "POSIX collating elements are not supported.");
        }
    }

    /// <summary>
    /// Finds the <c>:]</c>, <c>.]</c>, or <c>=]</c> that closes the POSIX construct opened at <paramref name="open"/>,
    /// the way PCRE looks for it, or returns -1.
    /// </summary>
    private int FindPcrePosixTerminator(int open, char terminator)
    {
        for (var index = open + 2; index < Text.Length; index++)
        {
            var ch = Text[index];
            if (ch == '\\' && index + 1 < Text.Length && Text[index + 1] is ']' or '\\')
            {
                index++;
            }
            else if ((ch == '[' && index + 1 < Text.Length && Text[index + 1] == terminator) || ch == ']')
            {
                return -1;
            }
            else if (ch == terminator && index + 1 < Text.Length && Text[index + 1] == ']')
            {
                return index;
            }
        }

        return -1;
    }

    // ---- the ECMAScript class set grammar ----

    /// <summary>Reads the members of a class under the <c>v</c> flag.</summary>
    /// <remarks>
    /// <para>
    /// The grammar is a union of members, or a single intersection, or a single difference; the operators take single
    /// operands, never ranges, and may not be mixed at one level. A class may contain another.
    /// </para>
    /// <para>
    /// A member may match a string rather than a character, which a negated class may not. Whether the class may
    /// is left in <see cref="LastSetMemberMayContainStrings"/> so the class around it can apply the same rule.
    /// </para>
    /// </remarks>
    private RegexCharacterClassSyntax ParseClassSetMembers(ScannedToken openBracketToken)
    {
        ScannedToken caretToken = default;
        if (Scanner.Current == '^')
        {
            var caretStart = Scanner.Position;
            Scanner.Position++;
            caretToken = Scanner.Token(SyntaxKind.CaretToken, caretStart);
        }

        var members = new List<RegexSyntaxNode>();
        var mayContainStrings = false;

        while (ClassSetOperatorLength(Scanner.Position) > 0)
        {
            members.Add(SkipClassSetOperator(atStart: true));
        }

        if (!IsAtClassSetEnd())
        {
            var first = ReadClassSetMember();
            if (ClassSetOperatorLength(Scanner.Position) > 0)
            {
                members.Add(ParseClassSetOperation(first, out mayContainStrings));
                if (!IsAtClassSetEnd())
                {
                    AddDiagnostic(new TextSpan(Scanner.Position, 1), RegexDiagnosticIds.MalformedClassSetOperation, "A class set operation cannot be combined with other members.");
                }
            }
            else
            {
                members.Add(first.Node);
                mayContainStrings = first.MayContainStrings;
            }

            while (!IsAtClassSetEnd())
            {
                // An operator turns the member before it into the first operand of a set operation. Only the first
                // member may be one, so any operator this far in has more than one member on its left.
                if (ClassSetOperatorLength(Scanner.Position) > 0)
                {
                    members.Add(SkipClassSetOperator(atStart: false));
                    continue;
                }

                var member = ReadClassSetMember();
                members.Add(member.Node);
                mayContainStrings |= member.MayContainStrings;
            }
        }

        ScannedToken closeBracketToken;
        if (Scanner.Current == ']')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            closeBracketToken = Scanner.Token(SyntaxKind.CloseBracketToken, closeStart);
        }
        else
        {
            AddDiagnostic(
                TextSpan.FromBounds(openBracketToken.Span.Start, Math.Max(openBracketToken.Span.Start, Scanner.Position)),
                RegexDiagnosticIds.UnterminatedBracket,
                "Unterminated character class: expected ']'.");
            closeBracketToken = Scanner.MissingToken(SyntaxKind.CloseBracketToken);
        }

        if (caretToken.IsPresent && mayContainStrings)
        {
            AddDiagnostic(
                TextSpan.FromBounds(openBracketToken.Span.Start, closeBracketToken.End),
                RegexDiagnosticIds.NegatedClassContainsStrings,
                "A negated character class cannot contain strings.");
        }

        // A negated class matches single characters whatever it contains, which is what lets a class around it be
        // negated in turn.
        LastSetMemberMayContainStrings = mayContainStrings && !caretToken.IsPresent;

        return new RegexCharacterClassSyntax(openBracketToken, caretToken, SyntaxFactory.ListNode([.. members]), closeBracketToken, Options);
    }

    private bool IsAtClassSetEnd() => Scanner.IsAtEnd || Scanner.Current == ']';

    /// <summary>The length of a class set operator at <paramref name="position"/>, or 0.</summary>
    /// <remarks>
    /// A single <c>-</c> is a range, so only the doubled form is an operator. The same is true of <c>&amp;</c>, which
    /// on its own is an ordinary character.
    /// </remarks>
    private int ClassSetOperatorLength(int position)
    {
        // Only the class set grammar has operators. Everywhere else "--" is an ordinary dash followed by another, and
        // suppressing the range look-ahead for it would quietly change what "[a--b]" means.
        if (!UsesUnicodeSetsMode || position + 1 >= Text.Length)
            return 0;

        var ch = Text[position];

        return (ch == '&' || ch == '-') && Text[position + 1] == ch ? 2 : 0;
    }

    /// <summary>
    /// Reads one member of a class set: a nested class, a string disjunction, a shorthand or property, a single
    /// character, or a range between two characters.
    /// </summary>
    private ClassSetMember ReadClassSetMember()
    {
        var start = Scanner.Position;

        if (Scanner.Current == '[')
            return new ClassSetMember(ParseNestedSetClass(), LastSetMemberMayContainStrings, IsRange: false);

        if (Scanner.Current == '\\' && Scanner.Peek() == 'q' && Scanner.Peek(2) == '{')
            return new ClassSetMember(ParseClassStringLiteral(), LastSetMemberMayContainStrings, IsRange: false);

        if (Scanner.Position + 1 < Text.Length && Scanner.Peek() == Scanner.Current && ReservedDoublePunctuators.Contains(Scanner.Current, StringComparison.Ordinal))
            return new ClassSetMember(SkipReservedDoublePunctuator(), MayContainStrings: false, IsRange: false);

        var element = ReadClassSetCharacter();
        if (element.IsClassEscape)
        {
            ReportClassAsRangeStart(element.Node);

            return new ClassSetMember(element.Node, LastSetMemberMayContainStrings, IsRange: false);
        }

        // "[a-]" is not a range: the dash has nothing after it, and on its own it is a syntax character.
        if (Scanner.Current != '-' || Scanner.Peek() is '-' or ']' || Scanner.Position + 1 >= Text.Length)
            return new ClassSetMember(element.Node, MayContainStrings: false, IsRange: false);

        var hyphenStart = Scanner.Position;
        Scanner.Position++;
        var hyphenToken = Scanner.Token(SyntaxKind.HyphenToken, hyphenStart);

        RegexSyntaxNode end;
        if (Scanner.Current == '[' || (Scanner.Current == '\\' && Scanner.Peek() == 'q' && Scanner.Peek(2) == '{'))
        {
            end = Scanner.Current == '[' ? ParseNestedSetClass() : ParseClassStringLiteral();
            var invalid = new RegexCharacterRangeSyntax(element.Node, hyphenToken, end, Options);
            AddDiagnostic(JustParsedSpan(invalid), RegexDiagnosticIds.ShorthandClassInCharacterRange, $"'{end}' cannot be an endpoint of a character range.");

            return new ClassSetMember(invalid, MayContainStrings: false, IsRange: true);
        }

        var endElement = ReadClassSetCharacter();
        var range = new RegexCharacterRangeSyntax(element.Node, hyphenToken, endElement.Node, Options);
        if (endElement.IsClassEscape)
        {
            AddDiagnostic(JustParsedSpan(endElement.Node), RegexDiagnosticIds.ShorthandClassInCharacterRange, $"Shorthand class '{endElement.Node}' cannot be an endpoint of a character range.");
        }
        else if (element.Value > endElement.Value)
        {
            AddDiagnostic(TextSpan.FromBounds(start, Scanner.Position), RegexDiagnosticIds.ReversedCharacterRange, $"Character range '{range}' is reversed.");
        }

        return new ClassSetMember(range, MayContainStrings: false, IsRange: true);
    }

    /// <summary>Reads one character of a class set, reporting a syntax character that should have been escaped.</summary>
    private ClassElement ReadClassSetCharacter()
    {
        var start = Scanner.Position;
        if (Scanner.Current != '\\' && ClassSetSyntaxCharacters.Contains(Scanner.Current, StringComparison.Ordinal))
        {
            Scanner.Position++;
            AddDiagnostic(new TextSpan(start, 1), RegexDiagnosticIds.InvalidClassSetCharacter, $"'{Text[start]}' must be escaped inside a character class.");

            return new ClassElement(new RegexLiteralSyntax(Scanner.Token(SyntaxKind.LiteralToken, start), Options), Text[start], IsClassEscape: false, IsDashEscape: false);
        }

        LastSetMemberMayContainStrings = false;

        return ReadClassElement();
    }

    /// <summary>Parses a class nested inside another, which only the class set grammar allows.</summary>
    private RegexSyntaxNode ParseNestedSetClass()
    {
        var start = Scanner.Position;
        LastSetMemberMayContainStrings = false;
        if (!TryEnterRecursion(new TextSpan(start, 1)))
        {
            Scanner.Position++;

            return new RegexLiteralSyntax(Scanner.Token(SyntaxKind.LiteralToken, start), Options);
        }

        try
        {
            Scanner.Position++;

            return ParseCharacterClassBody(Scanner.Token(SyntaxKind.OpenBracketToken, start));
        }
        finally
        {
            ExitRecursion();
        }
    }

    /// <summary>Parses <c>\q{abc|def}</c>, which contributes whole strings rather than characters.</summary>
    private RegexClassStringLiteralSyntax ParseClassStringLiteral()
    {
        var start = Scanner.Position;
        Scanner.Position += 3;
        var startToken = Scanner.Token(SyntaxKind.QuoteStartToken, start);

        // A backslash escapes the character after it, the closing brace included, so the scan cannot simply stop at
        // the first "}".
        var textStart = Scanner.Position;
        while (!Scanner.IsAtEnd && Scanner.Current != '}')
        {
            if (Scanner.Current == '\\' && Scanner.Peek() == 'u' && Scanner.Peek(2) == '{' && Text.IndexOf('}', Scanner.Position, StringComparison.Ordinal) is var codePointEnd && codePointEnd >= 0)
            {
                // "\u{…}" has a closing brace of its own, which does not close the disjunction.
                Scanner.Position = codePointEnd + 1;
                continue;
            }

            Scanner.Position += Scanner.Current == '\\' && Scanner.Position + 1 < Text.Length ? 2 : 1;
        }

        var textEnd = Scanner.Position;
        LastSetMemberMayContainStrings = ValidateClassStringContent(textStart, textEnd);
        Scanner.Position = textEnd;
        var textToken = Scanner.Position > textStart ? Scanner.Token(SyntaxKind.QuoteTextToken, textStart) : default;

        ScannedToken closeBraceToken = default;
        if (Scanner.Current == '}')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            closeBraceToken = Scanner.Token(SyntaxKind.CloseBraceToken, closeStart);
        }
        else
        {
            AddDiagnostic(
                TextSpan.FromBounds(start, Scanner.Position),
                RegexDiagnosticIds.UnterminatedBracket,
                "Unterminated '\\q{...}' string disjunction.");
        }

        return new RegexClassStringLiteralSyntax(startToken, textToken, closeBraceToken, Options);
    }

    /// <summary>Checks the body of a <c>\q{…}</c> disjunction, and returns whether any alternative is not one character.</summary>
    /// <remarks>The body is not free text: each character is read the way a class set character is.</remarks>
    private bool ValidateClassStringContent(int start, int end)
    {
        var mayContainStrings = false;
        var length = 0;
        Scanner.Position = start;
        while (Scanner.Position < end)
        {
            var ch = Scanner.Current;
            if (ch == '|')
            {
                mayContainStrings |= length != 1;
                length = 0;
                Scanner.Position++;
                continue;
            }

            if (ch == '\\')
            {
                // A backslash that ends the pattern escapes nothing; the unterminated disjunction is reported instead.
                Scanner.Position++;
                if (Scanner.Position < end)
                {
                    _ = ScanCharEscape();
                }

                length++;
                continue;
            }

            if (ClassSetSyntaxCharacters.Contains(ch, StringComparison.Ordinal))
            {
                AddDiagnostic(new TextSpan(Scanner.Position, 1), RegexDiagnosticIds.MalformedClassString, $"'{ch}' must be escaped inside a '\\q{{...}}' disjunction.");
            }
            else if (Scanner.Position + 1 < end && Scanner.Peek() == ch &&
                (ReservedDoublePunctuators.Contains(ch, StringComparison.Ordinal) || ch == '&'))
            {
                AddDiagnostic(new TextSpan(Scanner.Position, 2), RegexDiagnosticIds.ReservedClassSetPunctuator, $"'{ch}{ch}' is reserved and may not appear here.");
                Scanner.Position++;
            }

            Scanner.Position += char.IsHighSurrogate(ch) && Scanner.Position + 1 < end && char.IsLowSurrogate(Scanner.Peek()) ? 2 : 1;
            length++;
        }

        return mayContainStrings || length != 1;
    }

    /// <summary>Reports a doubled punctuator the class set grammar reserves.</summary>
    private RegexSkippedTextSyntax SkipReservedDoublePunctuator()
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var token = Scanner.Token(SyntaxKind.BadToken, start);
        AddDiagnostic(token.Span, RegexDiagnosticIds.ReservedClassSetPunctuator, $"'{token.Text}' is reserved and may not appear here.");

        return new RegexSkippedTextSyntax(token, Options);
    }

    /// <summary>
    /// Parses the rest of an intersection or difference, given the operand already read.
    /// </summary>
    /// <remarks>
    /// The grammar is n-ary but not mixed: <c>[a--b--c]</c> is one difference of three operands, while
    /// <c>[a&amp;&amp;b--c]</c> is an error. An intersection may match strings only when every operand may; a
    /// difference only when its first operand may.
    /// </remarks>
    private RegexClassSetOperationSyntax ParseClassSetOperation(ClassSetMember first, out bool mayContainStrings)
    {
        ReportRangeOperand(first);
        var operands = new List<RegexSyntaxNode> { first.Node };
        var operators = new List<ScannedToken>();
        mayContainStrings = first.MayContainStrings;

        string? expected = null;
        while (ClassSetOperatorLength(Scanner.Position) is var length && length > 0)
        {
            var operatorStart = Scanner.Position;
            Scanner.Position += length;
            var operatorToken = Scanner.Token(SyntaxKind.ClassSetOperatorToken, operatorStart);
            operators.Add(operatorToken);

            expected ??= operatorToken.Text;
            if (operatorToken.Text != expected)
            {
                AddDiagnostic(operatorToken.Span, RegexDiagnosticIds.MalformedClassSetOperation, "Class set operators may not be mixed at the same level.");
            }

            if (IsAtClassSetEnd())
            {
                AddDiagnostic(operatorToken.Span, RegexDiagnosticIds.MalformedClassSetOperation, "A class set operator needs an operand after it.");
                break;
            }

            // "&&&" is not an operator followed by "&": the grammar keeps a third "&" out so it can mean something later.
            if (operatorToken.Text == "&&" && Scanner.Current == '&')
            {
                AddDiagnostic(new TextSpan(Scanner.Position, 1), RegexDiagnosticIds.MalformedClassSetOperation, "'&&' cannot be followed by another '&'.");
            }

            var operand = ReadClassSetMember();
            ReportRangeOperand(operand);
            operands.Add(operand.Node);
            if (operatorToken.Text == "&&")
            {
                mayContainStrings &= operand.MayContainStrings;
            }
        }

        return new RegexClassSetOperationSyntax(Interleave(operands, operators), Options);

        void ReportRangeOperand(ClassSetMember member)
        {
            if (member.IsRange)
            {
                AddDiagnostic(JustParsedSpan(member.Node), RegexDiagnosticIds.MalformedClassSetOperation, "A range cannot be an operand of a class set operation; wrap it in a nested class.");
            }
        }
    }

    /// <summary>Keeps an operator that has no single operand before it, so the text is still accounted for.</summary>
    private RegexSkippedTextSyntax SkipClassSetOperator(bool atStart)
    {
        var start = Scanner.Position;
        Scanner.Position += ClassSetOperatorLength(start);
        var token = Scanner.Token(SyntaxKind.BadToken, start);

        AddDiagnostic(
            token.Span,
            RegexDiagnosticIds.MalformedClassSetOperation,
            atStart
                ? "A class set operator needs an operand before it."
                : "A class set operator takes a single operand on each side.");

        return new RegexSkippedTextSyntax(token, Options);
    }

    // ---- shared ----

    private void AddRange(List<RegexSyntaxNode> members, RegexSyntaxNode start, ScannedToken hyphen, ClassElement end, int startValue)
    {
        var range = new RegexCharacterRangeSyntax(start, hyphen, end.Node, Options);
        if (startValue > end.Value)
        {
            AddDiagnostic(JustParsedSpan(range), RegexDiagnosticIds.ReversedCharacterRange, $"Character range '{range}' is reversed.");
        }

        members.Add(range);
    }

    /// <summary>Parses the <c>[…]</c> a subtraction removes, which must be the last thing in its class.</summary>
    private RegexClassSubtractionSyntax ParseSubtraction(ScannedToken hyphenToken)
    {
        var start = Scanner.Position;
        RegexCharacterClassSyntax nested;

        if (TryEnterRecursion(new TextSpan(start, 1)))
        {
            try
            {
                Scanner.Position++;
                nested = ParseCharacterClassBody(Scanner.Token(SyntaxKind.OpenBracketToken, start));
            }
            finally
            {
                ExitRecursion();
            }
        }
        else
        {
            Scanner.Position++;
            var openBracketToken = Scanner.Token(SyntaxKind.OpenBracketToken, start);
            var rest = Scanner.Position;
            Scanner.Position = Text.Length;
            nested = new RegexCharacterClassSyntax(
                openBracketToken,
                caretToken: null,
                new RegexSkippedTextSyntax(Scanner.Token(SyntaxKind.BadToken, rest), Options),
                Scanner.MissingToken(SyntaxKind.CloseBracketToken), Options);
        }

        if (!Scanner.IsAtEnd && Scanner.Current != ']')
        {
            AddDiagnostic(JustParsedSpan(nested), RegexDiagnosticIds.ExclusionGroupNotLast, "A character class subtraction must be the last element of the character class.");
        }

        return new RegexClassSubtractionSyntax(hyphenToken, nested, Options);
    }

    /// <summary>The POSIX character class names every dialect that has bracket expressions knows.</summary>
    private static bool IsPosixClassName(string name) =>
        name is "alnum" or "alpha" or "blank" or "cntrl" or "digit" or "graph" or "lower" or "print" or "punct" or "space" or "upper" or "xdigit";

    /// <summary>Reads <c>[:alpha:]</c>, <c>[.ch.]</c>, or <c>[=a=]</c> for the dialects that have them.</summary>
    /// <remarks>
    /// POSIX commits as soon as it sees <c>[:</c>, <c>[.</c>, or <c>[=</c>, so a construct that is never closed is an
    /// error there. PCRE only treats the text as one when the terminator can be found, and otherwise reads the bracket
    /// as an ordinary character.
    /// </remarks>
    private bool TryParsePosixBracket(out RegexSyntaxNode node, out bool isCollatingSymbol)
    {
        node = null!;
        isCollatingSymbol = false;
        if (Scanner.Current != '[' || Scanner.Peek() is not (':' or '.' or '='))
            return false;

        var marker = Scanner.Peek();
        var start = Scanner.Position;
        int closeIndex;
        if (Dialect.Family == RegexDialectFamily.Posix)
        {
            // The name holds at least one character, which is what makes "[.].]" the collating symbol "]".
            closeIndex = start + 3 <= Text.Length ? Text.IndexOf($"{marker}]", start + 3, StringComparison.Ordinal) : -1;
            if (closeIndex < 0)
            {
                Scanner.Position = Text.Length;
                var unterminated = Scanner.Token(SyntaxKind.BadToken, start);
                AddDiagnostic(unterminated.Span, RegexDiagnosticIds.UnterminatedBracket, $"Unterminated '[{marker}' in a bracket expression: expected '{marker}]'.");
                node = new RegexSkippedTextSyntax(unterminated, Options);

                return true;
            }
        }
        else
        {
            closeIndex = FindPcrePosixTerminator(start, marker);
            if (closeIndex < 0)
                return false;
        }

        Scanner.Position += 2;
        var startToken = Scanner.Token(SyntaxKind.PosixClassStartToken, start);

        var nameStart = Scanner.Position;
        Scanner.Position = closeIndex;
        var nameToken = Scanner.Token(SyntaxKind.PosixClassNameToken, nameStart);

        var endStart = Scanner.Position;
        Scanner.Position += 2;
        var endToken = Scanner.Token(SyntaxKind.PosixClassEndToken, endStart);

        var name = nameToken.Text;
        if (marker == ':')
        {
            node = new RegexPosixCharacterClassSyntax(startToken, nameToken, endToken, Options);

            // PCRE adds "word" and "ascii", and lets "^" negate a class.
            var isPcre = Dialect.Family == RegexDialectFamily.Pcre;
            var bare = isPcre && name is ['^', ..] ? name[1..] : name;
            if (!IsPosixClassName(bare) && !(isPcre && bare is "word" or "ascii"))
            {
                AddDiagnostic(nameToken.Span, RegexDiagnosticIds.InvalidPosixBracketExpression, $"Unknown POSIX character class name '{name}'.");
            }
        }
        else
        {
            node = new RegexCollatingElementSyntax(startToken, nameToken, endToken, Options);
            isCollatingSymbol = marker == '.';
            if (Dialect.Family == RegexDialectFamily.Pcre)
            {
                AddDiagnostic(TextSpan.FromBounds(start, Scanner.Position), RegexDiagnosticIds.InvalidPosixBracketExpression, "POSIX collating elements are not supported.");
            }
            else if (name.Length != 1 && !(name.Length == 2 && char.IsSurrogatePair(name[0], name[1])))
            {
                AddDiagnostic(nameToken.Span, RegexDiagnosticIds.InvalidPosixBracketExpression, $"'{name}' is not a collating element.");
            }
        }

        return true;
    }

    /// <summary>The character a collating symbol such as <c>[.a.]</c> stands for.</summary>
    private static int CollatingSymbolValue(RegexSyntaxNode node)
    {
        var text = node.ToString();
        var name = text.Length >= 4 ? text[2..^2] : string.Empty;

        return name.Length switch
        {
            0 => 0,
            >= 2 when char.IsSurrogatePair(name[0], name[1]) => char.ConvertToUtf32(name[0], name[1]),
            _ => name[0],
        };
    }

    /// <summary>Reads one member of a class: a shorthand, a category, an escape, or a single character.</summary>
    private ClassElement ReadClassElement()
    {
        var start = Scanner.Position;

        if (Scanner.Current == '\\' && Scanner.Position + 1 < Text.Length && !BackslashIsLiteralInClass)
        {
            switch (Scanner.Peek())
            {
                case var letter when IsShorthandClassLetterInClass(letter):
                    Scanner.Position += 2;
                    return new ClassElement(new RegexCharacterClassEscapeSyntax(Scanner.Token(SyntaxKind.ClassEscapeToken, start), Options), 0, IsClassEscape: true, IsDashEscape: false);

                case 'p' or 'P' when SupportsUnicodeCategories:
                    LastSetMemberMayContainStrings = false;
                    return new ClassElement(ParseUnicodeCategory(leadingTrivia: null), 0, IsClassEscape: true, IsDashEscape: false);

                case '-':
                    Scanner.Position += 2;
                    return new ClassElement(new RegexCharacterEscapeSyntax(Scanner.Token(SyntaxKind.EscapeToken, start, leadingTrivia: null, "-"), Options), '-', IsClassEscape: false, IsDashEscape: true);

                default:
                    Scanner.Position++;
                    var value = ScanCharEscape();
                    return new ClassElement(
                        new RegexCharacterEscapeSyntax(Scanner.Token(SyntaxKind.EscapeToken, start, leadingTrivia: null, value), Options),
                        FirstCodePoint(value),
                        IsClassEscape: false,
                        IsDashEscape: false);
            }
        }

        Scanner.Position++;
        if (ReadsCodePoints && char.IsHighSurrogate(Text[start]) && char.IsLowSurrogate(Scanner.Current))
        {
            Scanner.Position++;
        }

        return new ClassElement(new RegexLiteralSyntax(Scanner.Token(SyntaxKind.LiteralToken, start), Options), FirstCodePoint(Text[start..Scanner.Position]), IsClassEscape: false, IsDashEscape: false);
    }

    /// <summary>The first code point of <paramref name="value"/>, or 0 when it is empty.</summary>
    private static int FirstCodePoint(string value) => value.Length switch
    {
        0 => 0,
        >= 2 when char.IsSurrogatePair(value[0], value[1]) => char.ConvertToUtf32(value[0], value[1]),
        _ => value[0],
    };

    /// <summary>One member of a class, with what the parser needs to know about it to read the next one.</summary>
    /// <param name="Value">The code point the member stands for, which is what a range compares.</param>
    private readonly record struct ClassElement(RegexSyntaxNode Node, int Value, bool IsClassEscape, bool IsDashEscape);

    /// <summary>One member of a class set, with whether it may match a string and whether it is a range.</summary>
    private readonly record struct ClassSetMember(RegexSyntaxNode Node, bool MayContainStrings, bool IsRange);
}
