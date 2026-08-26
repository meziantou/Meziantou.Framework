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
        // A few escapes are allowed inside a class and nowhere else, so the reader has to know where it is.
        var wasInCharacterClass = IsInCharacterClass;
        IsInCharacterClass = true;
        try
        {
            return ParseCharacterClassMembers(openBracketToken);
        }
        finally
        {
            IsInCharacterClass = wasInCharacterClass;
        }
    }

    private RegexCharacterClassSyntax ParseCharacterClassMembers(RegexSyntaxToken openBracketToken)
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

            // In the class set grammar a class may contain another one, and "\q{…}" contributes whole strings.
            if (UsesUnicodeSetsMode && !inRange && Scanner.Current == '[')
            {
                members.Add(ParseNestedSetClass());
                firstChar = false;
                continue;
            }

            if (UsesUnicodeSetsMode && !inRange && Scanner.Current == '\\' && Scanner.Peek() == 'q' && Scanner.Peek(2) == '{')
            {
                members.Add(ParseClassStringLiteral());
                firstChar = false;
                continue;
            }

            if (UsesUnicodeSetsMode && !inRange && Scanner.Position + 1 < Text.Length &&
                Scanner.Peek() == Scanner.Current &&
                ReservedDoublePunctuators.Contains(Scanner.Current, StringComparison.Ordinal))
            {
                members.Add(SkipReservedDoublePunctuator());
                firstChar = false;
                continue;
            }

            // An operator turns the member before it into the first operand of a set operation. An operand is a single
            // thing, so anything else on the left is an error, and the operator is kept as skipped text rather than
            // folded into an operation whose parts would no longer be in source order.
            if (UsesUnicodeSetsMode && !inRange && ClassSetOperatorLength(Scanner.Position) > 0)
            {
                if (members.Count == 1)
                {
                    members = [ParseClassSetOperation(members[0])];
                }
                else
                {
                    members.Add(SkipClassSetOperator(members.Count == 0));
                }

                firstChar = false;
                continue;
            }

            var element = ReadClassElement();

            if (element.IsClassEscape)
            {
                if (inRange && AllowsShorthandClassInRange)
                {
                    // Where this is allowed there is no range at all: the dash between them is an ordinary member.
                    members.Add(rangeStart!);
                    members.Add(WithOptions(new RegexLiteralSyntax(
                        new RegexSyntaxToken(RegexSyntaxKind.LiteralToken, rangeHyphen!.Text, fullStart: rangeHyphen.FullSpan.Start))));
                    members.Add(element.Node);
                    inRange = false;
                }
                else if (inRange)
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
            else if (Scanner.Position + 1 < Text.Length && Text[Scanner.Position] == '-' && Text[Scanner.Position + 1] != ']' &&
                ClassSetOperatorLength(Scanner.Position) == 0)
            {
                // The look-ahead has to decline the first "-" of a "--" operator, or "[a--b]" starts a range from "a"
                // to "-" and the operator never gets the chance to be one.
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

    /// <summary>Parses a class nested inside another, which only the class set grammar allows.</summary>
    private RegexSyntaxNode ParseNestedSetClass()
    {
        var start = Scanner.Position;
        if (!TryEnterRecursion(new TextSpan(start, 1)))
        {
            Scanner.Position++;

            return WithOptions(new RegexLiteralSyntax(Scanner.Token(RegexSyntaxKind.LiteralToken, start)));
        }

        try
        {
            Scanner.Position++;

            return ParseCharacterClassBody(Scanner.Token(RegexSyntaxKind.OpenBracketToken, start));
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
        var startToken = Scanner.Token(RegexSyntaxKind.QuoteStartToken, start);

        // A backslash escapes the character after it, the closing brace included, so the scan cannot simply stop at
        // the first "}".
        var textStart = Scanner.Position;
        while (!Scanner.IsAtEnd && Scanner.Current != '}')
        {
            Scanner.Position += Scanner.Current == '\\' && Scanner.Position + 1 < Text.Length ? 2 : 1;
        }

        ValidateClassStringContent(textStart, Scanner.Position);
        var textToken = Scanner.Position > textStart ? Scanner.Token(RegexSyntaxKind.QuoteTextToken, textStart) : null;

        RegexSyntaxToken? closeBraceToken = null;
        if (Scanner.Current == '}')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            closeBraceToken = Scanner.Token(RegexSyntaxKind.CloseBraceToken, closeStart);
        }
        else
        {
            AddDiagnostic(
                TextSpan.FromBounds(start, Scanner.Position),
                RegexDiagnosticIds.UnterminatedBracket,
                "Unterminated '\\q{...}' string disjunction.");
        }

        return WithOptions(new RegexClassStringLiteralSyntax(startToken, textToken, closeBraceToken));
    }

    /// <summary>The characters a class set has to have escaped, because unescaped they mean something else.</summary>
    private const string ClassSetSyntaxCharacters = "()[]{}/-";

    /// <summary>
    /// The characters that may not appear doubled. The grammar reserves them so the syntax can grow later without
    /// changing what an existing pattern means.
    /// </summary>
    /// <remarks><c>&amp;</c> and <c>-</c> are not here: doubled they are the operators, which are read before this.</remarks>
    private const string ReservedDoublePunctuators = "!#$%*+,.:;<=>?@^`~";

    /// <summary>Checks the body of a <c>\q{…}</c> disjunction, which is not free text.</summary>
    private void ValidateClassStringContent(int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            var ch = Text[index];
            if (ch == '\\')
            {
                // Whatever follows a backslash is that character, so it needs no checking of its own.
                index++;
                continue;
            }

            if (ClassSetSyntaxCharacters.Contains(ch, StringComparison.Ordinal))
            {
                AddDiagnostic(new TextSpan(index, 1), RegexDiagnosticIds.MalformedClassString, $"'{ch}' must be escaped inside a '\\q{{...}}' disjunction.");
            }
            else if (index + 1 < end && Text[index + 1] == ch &&
                (ReservedDoublePunctuators.Contains(ch, StringComparison.Ordinal) || ch == '&'))
            {
                AddDiagnostic(new TextSpan(index, 2), RegexDiagnosticIds.ReservedClassSetPunctuator, $"'{ch}{ch}' is reserved and may not appear here.");
                index++;
            }
        }
    }

    /// <summary>Reports a doubled punctuator the class set grammar reserves.</summary>
    private RegexSkippedTextSyntax SkipReservedDoublePunctuator()
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var token = Scanner.Token(RegexSyntaxKind.BadToken, start);
        AddDiagnostic(token.Span, RegexDiagnosticIds.ReservedClassSetPunctuator, $"'{token.Text}' is reserved and may not appear here.");

        return WithOptions(new RegexSkippedTextSyntax([token], token.FullSpan.Start));
    }

    /// <summary>
    /// Parses the rest of an intersection or difference, given the operand already read.
    /// </summary>
    /// <remarks>
    /// The grammar is n-ary but not mixed: <c>[a--b--c]</c> is one difference of three operands, while
    /// <c>[a&amp;&amp;b--c]</c> is an error. An operand is a single thing, so <c>[abc--d]</c> is an error too, which is
    /// why more than one member on the left is reported rather than quietly grouped.
    /// </remarks>
    private RegexClassSetOperationSyntax ParseClassSetOperation(RegexSyntaxNode first)
    {
        var operands = new List<RegexSyntaxNode> { first };
        var operators = new List<RegexSyntaxToken>();
        var start = first.FullSpan.Start;

        string? expected = null;
        while (ClassSetOperatorLength(Scanner.Position) is var length && length > 0)
        {
            var operatorStart = Scanner.Position;
            Scanner.Position += length;
            var operatorToken = Scanner.Token(RegexSyntaxKind.ClassSetOperatorToken, operatorStart);
            operators.Add(operatorToken);

            expected ??= operatorToken.Text;
            if (operatorToken.Text != expected)
            {
                AddDiagnostic(operatorToken.Span, RegexDiagnosticIds.MalformedClassSetOperation, "Class set operators may not be mixed at the same level.");
            }

            if (Scanner.IsAtEnd || Scanner.Current == ']')
            {
                AddDiagnostic(operatorToken.Span, RegexDiagnosticIds.MalformedClassSetOperation, "A class set operator needs an operand after it.");
                break;
            }

            operands.Add(ReadClassSetOperand());
        }

        return WithOptions(new RegexClassSetOperationSyntax(operands, operators, start));
    }

    /// <summary>Keeps an operator that has no single operand before it, so the text is still accounted for.</summary>
    private RegexSkippedTextSyntax SkipClassSetOperator(bool atStart)
    {
        var start = Scanner.Position;
        Scanner.Position += ClassSetOperatorLength(start);
        var token = Scanner.Token(RegexSyntaxKind.BadToken, start);

        AddDiagnostic(
            token.Span,
            RegexDiagnosticIds.MalformedClassSetOperation,
            atStart
                ? "A class set operator needs an operand before it."
                : "A class set operator takes a single operand on each side.");

        return WithOptions(new RegexSkippedTextSyntax([token], token.FullSpan.Start));
    }

    /// <summary>Reads one operand of a set operation: a nested class, a string disjunction, or a single member.</summary>
    private RegexSyntaxNode ReadClassSetOperand()
    {
        if (Scanner.Current == '[')
            return ParseNestedSetClass();

        if (Scanner.Current == '\\' && Scanner.Peek() == 'q' && Scanner.Peek(2) == '{')
            return ParseClassStringLiteral();

        var element = ReadClassElement();

        // An operand may itself be a range, as the right-hand side of "[\w--a-z]" is.
        if (!element.IsClassEscape && Scanner.Position + 1 < Text.Length && Text[Scanner.Position] == '-' && Text[Scanner.Position + 1] != ']' &&
            ClassSetOperatorLength(Scanner.Position) == 0)
        {
            var hyphenStart = Scanner.Position;
            Scanner.Position++;
            var hyphenToken = Scanner.Token(RegexSyntaxKind.HyphenToken, hyphenStart);
            var end = ReadClassElement();

            return WithOptions(new RegexCharacterRangeSyntax(element.Node, hyphenToken, end.Node));
        }

        return element.Node;
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
                case var letter when IsShorthandClassLetterInClass(letter):
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
