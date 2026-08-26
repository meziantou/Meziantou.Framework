// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
//
// Changes: ScanCharClass builds member nodes instead of a RegexCharClass, and the explicit stack of parent classes
// becomes recursion bounded by RegexParseOptions.MaxRecursionDepth. Which characters it consumes is unchanged.

namespace Meziantou.Framework.Language.Regex.Internals;

internal abstract partial class PerlStyleRegexParser
{
    /// <summary>Parses a character class, guarding against input that nests subtractions without end.</summary>
    private RegexAtomSyntax ParseCharacterClass(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;
        if (!TryEnterRecursion(new TextSpan(start, 1)))
            return ConsumeRestAsText(start, leadingTrivia);

        try
        {
            Scanner.Position++;

            return ParseCharacterClassBody(Scanner.Token(RegexSyntaxKind.OpenBracketToken, start, leadingTrivia));
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
    private RegexCharacterClassSyntax ParseCharacterClassBody(RegexSyntaxToken openBracketToken)
    {
        RegexSyntaxToken? caretToken = null;
        var firstChar = true;

        if (Scanner.Current == '^')
        {
            var caretStart = Scanner.Position;
            Scanner.Position++;
            caretToken = Scanner.Token(RegexSyntaxKind.CaretToken, caretStart);

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

        var members = new List<RegexSyntaxNode>();
        RegexSyntaxNode? rangeStart = null;
        RegexSyntaxToken? rangeHyphen = null;
        var rangeStartValue = '\0';
        var inRange = false;
        RegexSyntaxToken? closeBracketToken = null;

        while (!Scanner.IsAtEnd)
        {
            if (Scanner.Current == ']' && !firstChar)
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                closeBracketToken = Scanner.Token(RegexSyntaxKind.CloseBracketToken, closeStart);
                break;
            }

            // A subtraction reached through a range dash, as in "[a-[b]]": the dash was already claimed by the
            // look-ahead that started the range, so the character it would have ranged to is a plain member instead.
            if (!firstChar && inRange && Scanner.Current == '[' && Flavor.HasFeature(RegexFlavorFeatures.CharacterClassSubtraction))
            {
                inRange = false;
                members.Add(rangeStart!);
                members.Add(ParseSubtraction(rangeHyphen!));
                firstChar = false;
                continue;
            }

            // A subtraction with a dash of its own, as in "[a-z-[b]]".
            if (!firstChar && !inRange && Scanner.Current == '-' && Scanner.Peek() == '[' && Flavor.HasFeature(RegexFlavorFeatures.CharacterClassSubtraction))
            {
                var hyphenStart = Scanner.Position;
                Scanner.Position++;
                members.Add(ParseSubtraction(Scanner.Token(RegexSyntaxKind.HyphenToken, hyphenStart)));
                firstChar = false;
                continue;
            }

            if (!inRange && Flavor.HasFeature(RegexFlavorFeatures.PosixBracketExpressions) && TryParsePosixBracket(out var bracket))
            {
                members.Add(bracket);
                firstChar = false;
                continue;
            }

            var element = ReadClassElement();

            if (element.IsClassEscape)
            {
                if (inRange)
                {
                    // The engine rejects a shorthand class as a range endpoint outright. Keeping the range in the tree
                    // is the recovery: it accounts for every character, and the diagnostic says what is wrong with it.
                    AddDiagnostic(element.Node.Span, RegexDiagnosticIds.ShorthandClassInCharacterRange, $"Shorthand class '{element.Node}' cannot be an endpoint of a character range.");
                    members.Add(WithOptions(new RegexCharacterRangeSyntax(rangeStart!, rangeHyphen!, element.Node)));
                    inRange = false;
                }
                else
                {
                    members.Add(element.Node);
                }

                firstChar = false;
                continue;
            }

            // "\-" completes a range or stands for a literal dash, but never starts a range: the engine handles it
            // before the look-ahead that a plain character would go through.
            if (element.IsDashEscape)
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
            }
            else if (Scanner.Position + 1 < Text.Length && Text[Scanner.Position] == '-' && Text[Scanner.Position + 1] != ']')
            {
                var hyphenStart = Scanner.Position;
                Scanner.Position++;
                rangeStart = element.Node;
                rangeStartValue = element.Value;
                rangeHyphen = Scanner.Token(RegexSyntaxKind.HyphenToken, hyphenStart);
                inRange = true;
            }
            else
            {
                members.Add(element.Node);
            }

            firstChar = false;
        }

        if (inRange)
        {
            // Unreachable while the look-ahead that starts a range demands a character after the dash, but a class that
            // lost its endpoint must still account for the two members it does have.
            members.Add(rangeStart!);
            members.Add(WithOptions(new RegexCharacterRangeSyntax(rangeStart!, rangeHyphen!, null)));
        }

        if (closeBracketToken is null)
        {
            AddDiagnostic(
                TextSpan.FromBounds(openBracketToken.Span.Start, Math.Max(openBracketToken.Span.Start, Scanner.Position)),
                RegexDiagnosticIds.UnterminatedBracket,
                "Unterminated character class: expected ']'.");
            closeBracketToken = Scanner.MissingToken(RegexSyntaxKind.CloseBracketToken);
        }

        return WithOptions(new RegexCharacterClassSyntax(openBracketToken, caretToken, members, closeBracketToken));
    }

    private void AddRange(List<RegexSyntaxNode> members, RegexSyntaxNode start, RegexSyntaxToken hyphen, ClassElement end, char startValue)
    {
        var range = WithOptions(new RegexCharacterRangeSyntax(start, hyphen, end.Node));
        if (startValue > end.Value)
        {
            AddDiagnostic(range.Span, RegexDiagnosticIds.ReversedCharacterRange, $"Character range '{range}' is reversed.");
        }

        members.Add(range);
    }

    /// <summary>Parses the <c>[…]</c> a subtraction removes, which must be the last thing in its class.</summary>
    private RegexClassSubtractionSyntax ParseSubtraction(RegexSyntaxToken hyphenToken)
    {
        var start = Scanner.Position;
        RegexCharacterClassSyntax nested;

        if (TryEnterRecursion(new TextSpan(start, 1)))
        {
            try
            {
                Scanner.Position++;
                nested = ParseCharacterClassBody(Scanner.Token(RegexSyntaxKind.OpenBracketToken, start));
            }
            finally
            {
                ExitRecursion();
            }
        }
        else
        {
            Scanner.Position++;
            var openBracketToken = Scanner.Token(RegexSyntaxKind.OpenBracketToken, start);
            var rest = Scanner.Position;
            Scanner.Position = Text.Length;
            nested = WithOptions(new RegexCharacterClassSyntax(
                openBracketToken,
                caretToken: null,
                [WithOptions(new RegexSkippedTextSyntax([Scanner.Token(RegexSyntaxKind.BadToken, rest)], rest))],
                Scanner.MissingToken(RegexSyntaxKind.CloseBracketToken)));
        }

        if (!Scanner.IsAtEnd && Scanner.Current != ']')
        {
            AddDiagnostic(nested.Span, RegexDiagnosticIds.ExclusionGroupNotLast, "A character class subtraction must be the last element of the character class.");
        }

        return WithOptions(new RegexClassSubtractionSyntax(hyphenToken, nested));
    }

    /// <summary>Reads <c>[:alpha:]</c>, <c>[.ch.]</c>, or <c>[=a=]</c> for the flavors that have them.</summary>
    private bool TryParsePosixBracket(out RegexSyntaxNode node)
    {
        node = null!;
        if (Scanner.Current != '[' || Scanner.Peek() is not (':' or '.' or '='))
            return false;

        var marker = Scanner.Peek();
        var terminator = $"{marker}]";
        var closeIndex = Text.IndexOf(terminator, Scanner.Position + 2, StringComparison.Ordinal);
        if (closeIndex < 0)
            return false;

        var start = Scanner.Position;
        Scanner.Position += 2;
        var startToken = Scanner.Token(RegexSyntaxKind.PosixClassStartToken, start);

        var nameStart = Scanner.Position;
        Scanner.Position = closeIndex;
        var nameToken = Scanner.Token(RegexSyntaxKind.PosixClassNameToken, nameStart);

        var endStart = Scanner.Position;
        Scanner.Position += 2;
        var endToken = Scanner.Token(RegexSyntaxKind.PosixClassEndToken, endStart);

        node = marker == ':'
            ? WithOptions(new RegexPosixCharacterClassSyntax(startToken, nameToken, endToken))
            : WithOptions(new RegexCollatingElementSyntax(startToken, nameToken, endToken));

        return true;
    }

    /// <summary>Reads one member of a class: a shorthand, a category, an escape, or a single character.</summary>
    private ClassElement ReadClassElement()
    {
        var start = Scanner.Position;

        if (Scanner.Current == '\\' && Scanner.Position + 1 < Text.Length)
        {
            switch (Scanner.Peek())
            {
                case 'd':
                case 'D':
                case 's':
                case 'S':
                case 'w':
                case 'W':
                    Scanner.Position += 2;
                    return new ClassElement(WithOptions(new RegexCharacterClassEscapeSyntax(Scanner.Token(RegexSyntaxKind.ClassEscapeToken, start))), '\0', IsTranslated: false, IsClassEscape: true, IsDashEscape: false);

                case 'p' or 'P' when SupportsUnicodeCategories:
                    return new ClassElement(ParseUnicodeCategory([]), '\0', IsTranslated: false, IsClassEscape: true, IsDashEscape: false);

                case '-':
                    Scanner.Position += 2;
                    return new ClassElement(WithOptions(new RegexCharacterEscapeSyntax(Scanner.Token(RegexSyntaxKind.EscapeToken, start, leadingTrivia: null, "-"))), '-', IsTranslated: false, IsClassEscape: false, IsDashEscape: true);

                default:
                    Scanner.Position++;
                    var value = ScanCharEscape();
                    return new ClassElement(
                        WithOptions(new RegexCharacterEscapeSyntax(Scanner.Token(RegexSyntaxKind.EscapeToken, start, leadingTrivia: null, value))),
                        value.Length > 0 ? value[0] : '\0',
                        IsTranslated: true,
                        IsClassEscape: false,
                        IsDashEscape: false);
            }
        }

        var ch = Scanner.Current;
        Scanner.Position++;

        return new ClassElement(WithOptions(new RegexLiteralSyntax(Scanner.Token(RegexSyntaxKind.LiteralToken, start))), ch, IsTranslated: false, IsClassEscape: false, IsDashEscape: false);
    }

    /// <summary>One member of a class, with what the parser needs to know about it to read the next one.</summary>
    /// <param name="IsTranslated">
    /// Whether the character came from an escape. A <c>\[</c> does not open a subtraction the way a bare <c>[</c> does.
    /// </param>
    private readonly record struct ClassElement(RegexSyntaxNode Node, char Value, bool IsTranslated, bool IsClassEscape, bool IsDashEscape);
}
