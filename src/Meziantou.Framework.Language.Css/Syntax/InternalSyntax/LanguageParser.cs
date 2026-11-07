using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Language.Css.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>Builds the immutable tree for a style sheet, keeping every character of it.</summary>
/// <remarks>
/// <para>
/// The structure follows the parser of CSS Syntax Level 3 as revised for nesting: a block holds declarations and rules
/// side by side, and something that starts like a declaration is one unless it holds a <c>{}</c> block next to other
/// values, in which case it is a nested rule. What a browser throws away is kept too, as a node that says so and a
/// diagnostic that says why.
/// </para>
/// <para>
/// The tokenizer is context-free, so the text is tokenized once, up front, and the brackets are matched then as well.
/// Every construct is then parsed within a range of tokens: a block between its braces, a prelude up to its block. A
/// grammar that fails to match a range -- a selector list, a media query -- is tried again as plain component values
/// over the same range, so that a mistake never spreads past the construct it is in.
/// </para>
/// </remarks>
internal sealed partial class LanguageParser
{
    private readonly GreenToken[] _tokens;
    private readonly int[] _starts;
    private readonly int[] _match;
    private readonly CssParseOptions _options;
    private readonly int _endOfFile;
    private readonly List<PendingDiagnostic> _pending = [];
    private int _index;
    private int _limit;
    private int _depth;
    private PendingDiagnostic? _failure;

    /// <summary>The state of the top level of the style sheet, which decides where <c>@import</c> and <c>@namespace</c> may go.</summary>
    private TopLevelState _topLevelState;

    /// <summary>The namespace prefixes the <c>@namespace</c> rules of the style sheet declare, which a selector may use. They are case-sensitive.</summary>
    private readonly HashSet<string> _namespacePrefixes = new(StringComparer.Ordinal);

    public LanguageParser(SourceText source, CssParseOptions options)
    {
        _options = options;
        var tokens = new Lexer(source).LexAll();
        _tokens = [.. tokens];
        _endOfFile = _tokens.Length - 1;
        _limit = _endOfFile;
        _starts = new int[_tokens.Length];
        var position = 0;
        for (var i = 0; i < _tokens.Length; i++)
        {
            _starts[i] = position;
            position += _tokens[i].FullWidth;
        }

        _match = MatchBrackets(_tokens);
    }

    private enum TopLevelState
    {
        /// <summary>Nothing but <c>@charset</c>, <c>@import</c>, and <c>@layer</c> statements so far.</summary>
        Imports,

        /// <summary>A <c>@namespace</c> rule has been seen, so <c>@import</c> may no longer come.</summary>
        Namespaces,

        /// <summary>Any other rule has been seen.</summary>
        Rules,
    }

    private SyntaxKind CurrentKind => KindAt(_index);

    private bool AtEnd => _index >= _limit;

    private GreenToken Current => _tokens[_index];

    /// <summary>Parses the whole text as what <see cref="CssParseOptions.SourceKind"/> says it is.</summary>
    public CssStyleSheetSyntax ParseStyleSheet()
    {
        var mark = _pending.Count;
        var context = _options.SourceKind == CssSourceKind.DeclarationList ? BlockContext.DeclarationsOnly : BlockContext.TopLevel;
        var statements = ParseStatements(context, isTopLevel: true);
        var node = new CssStyleSheetSyntax(SyntaxFactory.List(statements), _tokens[_endOfFile]);

        return (CssStyleSheetSyntax)Finish(node, 0, mark);
    }

    /// <summary>Gets the end-of-file token, which holds the trivia after the last token.</summary>
    internal GreenToken EndOfFileToken => _tokens[_endOfFile];

    /// <summary>Parses the whole text as a list of component values, for the tests of CSS Syntax Level 3.</summary>
    internal List<GreenNode?> ParseComponentValueList() => ParseComponentValues(_limit);

    /// <summary>Parses the whole text as a single declaration whose value runs to the end of the text, as CSS Syntax Level 3 did in 2021.</summary>
    /// <returns>The declaration, or <see langword="null"/> when the text does not start with a name and a colon.</returns>
    internal CssDeclarationSyntax? ParseSingleDeclaration()
    {
        if (CurrentKind != SyntaxKind.IdentToken || KindAt(_index + 1) != SyntaxKind.ColonToken)
            return null;

        return ParseDeclaration(BlockContext.Unknown, _limit);
    }

    /// <summary>Reads the whole text as An+B, for the tests of CSS Syntax Level 3.</summary>
    internal bool TryParseAnPlusB(out int a, out int b) => TryReadAnPlusB(0, _endOfFile, out a, out b);

    // ------------------------------------------------------------------------------------------------------------
    // Statements: the content of a style sheet or of a block.
    // ------------------------------------------------------------------------------------------------------------

    private List<GreenNode?> ParseStatements(BlockContext context, bool isTopLevel)
    {
        var statements = new List<GreenNode?>();
        var nested = !isTopLevel || context != BlockContext.TopLevel;
        while (!AtEnd)
        {
            var start = _index;
            switch (CurrentKind)
            {
                case SyntaxKind.CdoToken or SyntaxKind.CdcToken when !nested:
                    statements.Add(new CssIgnoredTokenSyntax(EatToken()));
                    break;

                case SyntaxKind.SemicolonToken when nested:
                    statements.Add(new CssIgnoredTokenSyntax(EatToken()));
                    break;

                case SyntaxKind.CloseBraceToken when nested:
                    // Only the top level of a declaration list can hold a closing brace that closes nothing.
                    var mark = _pending.Count;
                    AddError(_index, CssDiagnosticDescriptors.UnexpectedToken, "}");
                    statements.Add(Finish(new CssIgnoredTokenSyntax(SyntaxFactory.AsSkippedText(EatToken())), _starts[start], mark));
                    break;

                case SyntaxKind.AtKeywordToken:
                    statements.Add(ParseAtRule(context, isTopLevel));
                    break;

                default:
                    if (nested && LooksLikeDeclaration(_index))
                    {
                        statements.Add(ParseDeclaration(context, FindDeclarationEnd(_index)));
                    }
                    else
                    {
                        statements.Add(ParseQualifiedRule(context, nested));
                    }

                    break;
            }

            // Every statement reads at least one token; if one ever does not, the token is skipped rather than read forever.
            if (_index == start && !AtEnd)
            {
                statements.Add(new CssSkippedTextSyntax(SyntaxFactory.ListNode([SyntaxFactory.AsSkippedText(EatToken())])));
            }
        }

        return statements;
    }

    /// <summary>Determines whether the tokens at <paramref name="index"/> are a declaration rather than a nested rule.</summary>
    /// <remarks>
    /// This is what CSS Syntax calls trying to consume a declaration and falling back to a rule. A name and a colon
    /// start a declaration, which a custom property always is. Any other one is a rule instead when its value holds a
    /// <c>{}</c> block together with anything else -- <c>a:hover { }</c> -- since no property allows that.
    /// </remarks>
    private bool LooksLikeDeclaration(int index)
    {
        if (KindAt(index) != SyntaxKind.IdentToken || KindAt(index + 1) != SyntaxKind.ColonToken)
            return false;

        if (SyntaxFacts.IsCustomPropertyName(_tokens[index].ValueText))
            return true;

        var braceBlocks = 0;
        var others = 0;
        var last = -1;
        var beforeLast = -1;
        var i = index + 2;
        while (i < _limit && KindAt(i) is not (SyntaxKind.SemicolonToken or SyntaxKind.CloseBraceToken))
        {
            if (KindAt(i) == SyntaxKind.OpenBraceToken)
            {
                braceBlocks++;
            }
            else
            {
                others++;
            }

            beforeLast = last;
            last = i;
            i = SkipComponentValue(i);
        }

        // "!important" is not part of the value it ends.
        if (beforeLast >= 0 && IsImportant(beforeLast, last))
        {
            others -= 2;
        }

        return braceBlocks == 0 || braceBlocks + others == 1;
    }

    /// <summary>Finds where a declaration starting at <paramref name="index"/> ends: at its semicolon, or at the end of the block.</summary>
    private int FindDeclarationEnd(int index)
    {
        var i = index;
        while (i < _limit && KindAt(i) is not (SyntaxKind.SemicolonToken or SyntaxKind.CloseBraceToken))
        {
            i = SkipComponentValue(i);
        }

        return i;
    }

    private bool IsImportant(int exclamation, int important)
        => KindAt(exclamation) == SyntaxKind.ExclamationToken && KindAt(important) == SyntaxKind.IdentToken && CssIdentifier.EqualsIgnoreAsciiCase(_tokens[important].ValueText, "important");

    /// <summary>Parses a declaration whose value ends at <paramref name="end"/>, which is its semicolon or the end of the block.</summary>
    private CssDeclarationSyntax ParseDeclaration(BlockContext context, int end)
    {
        var start = _index;
        var mark = _pending.Count;
        var nameIndex = _index;
        var name = EatToken();
        var colon = EatToken();

        // The last two values are "!important" when they are "!" and "important", whatever is between them.
        var valueEnd = end;
        var last = -1;
        var beforeLast = -1;
        for (var i = _index; i < end; i = SkipComponentValue(i))
        {
            beforeLast = last;
            last = i;
        }

        var hasImportant = beforeLast >= 0 && IsImportant(beforeLast, last) && SkipComponentValue(last) == end;
        if (hasImportant)
        {
            valueEnd = beforeLast;
        }

        var values = ParseComponentValues(valueEnd);
        GreenNode? important = null;
        if (hasImportant)
        {
            important = new CssImportantSyntax(EatToken(), EatToken());
        }

        var semicolon = _index == end && end < _limit && KindAt(end) == SyntaxKind.SemicolonToken ? EatToken() : null;
        var isCustomProperty = SyntaxFacts.IsCustomPropertyName(name.ValueText);
        if (!isCustomProperty)
        {
            if (values.Count == 0)
            {
                AddError(nameIndex, CssDiagnosticDescriptors.EmptyDeclarationValue, name.ValueText);
            }

            foreach (var value in values)
            {
                if (value is CssTokenValueSyntax && value.GetSlot(0)?.RawKind == (int)SyntaxKind.ExclamationToken)
                {
                    AddError(IndexOfToken(start, value), CssDiagnosticDescriptors.InvalidImportant);
                    break;
                }
            }
        }

        if (!AllowsDeclarations(context))
        {
            AddErrorSpan(start, _index, CssDiagnosticDescriptors.DeclarationNotAllowed, DescribeContext(context));
        }

        var node = new CssDeclarationSyntax(name, colon, SyntaxFactory.List(values), important, semicolon);

        return (CssDeclarationSyntax)Finish(node, _starts[start], mark);
    }

    /// <summary>Finds the index of the first token of <paramref name="node"/>, a node built from the tokens from <paramref name="from"/> onward.</summary>
    private int IndexOfToken(int from, GreenNode node)
    {
        var first = node.GetFirstTerminal();
        for (var i = from; i < _index; i++)
        {
            if (ReferenceEquals(_tokens[i], first))
                return i;
        }

        return from;
    }

    /// <summary>Parses a qualified rule: a prelude up to a <c>{}</c> block.</summary>
    /// <param name="context">Where the rule is.</param>
    /// <param name="nested">
    /// Whether the rule is in a block, where a <c>;</c> ends it and where a prelude that never reaches a block is what
    /// remains of an invalid declaration.
    /// </param>
    private GreenNode ParseQualifiedRule(BlockContext context, bool nested)
    {
        var start = _index;
        var mark = _pending.Count;
        var end = start;
        var hasStrayCloseBrace = false;
        while (end < _limit && KindAt(end) != SyntaxKind.OpenBraceToken && !(nested && KindAt(end) is SyntaxKind.SemicolonToken or SyntaxKind.CloseBraceToken))
        {
            if (KindAt(end) == SyntaxKind.CloseBraceToken)
            {
                // At the top level, a closing brace that closes nothing becomes part of the prelude of the next rule.
                AddError(end, CssDiagnosticDescriptors.UnexpectedToken, "}");
                hasStrayCloseBrace = true;
            }

            end = SkipComponentValue(end);
        }

        if (end >= _limit || KindAt(end) != SyntaxKind.OpenBraceToken)
        {
            if (nested)
            {
                // A browser drops this, along with the semicolon that ends it.
                MarkSkipped(start);
                AddErrorSpan(start, end, CssDiagnosticDescriptors.InvalidDeclaration, TextOf(start, end));
                var values = ParseComponentValues(end);
                var semicolon = _index < _limit && CurrentKind == SyntaxKind.SemicolonToken ? EatToken() : null;
                return Finish(new CssBadDeclarationSyntax(SyntaxFactory.List(values), semicolon), _starts[start], mark);
            }

            // A prelude that runs to the end of the text never gets its block.
            MarkSkipped(start);
            var prelude = ParseGenericPrelude(end);
            if (!hasStrayCloseBrace)
            {
                AddMissingToken(CssDiagnosticDescriptors.ExpectedToken, "{");
            }

            var missingBlock = new CssBlockSyntax(SyntaxFactory.MissingToken(SyntaxKind.OpenBraceToken), statements: null, SyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken));
            return Finish(new CssQualifiedRuleSyntax(GetQualifiedRuleKind(context), prelude, missingBlock), _starts[start], mark);
        }

        var kind = GetQualifiedRuleKind(context);
        GreenNode? preludeNode;
        if (LooksLikeCustomProperty(start, end))
        {
            // "--foo:hover { }" is consistently invalid: it cannot be a declaration here, and must not be a rule.
            MarkSkipped(start);
            AddErrorSpan(start, end, CssDiagnosticDescriptors.CustomPropertyLikeRule);
            preludeNode = ParseGenericPrelude(end);
            kind = SyntaxKind.QualifiedRule;
        }
        else
        {
            preludeNode = ParseQualifiedRulePrelude(kind, context, end);
        }

        if (!AllowsQualifiedRules(context))
        {
            AddErrorSpan(start, end, CssDiagnosticDescriptors.RuleNotAllowed, "A rule", DescribeContext(context));
        }

        if (kind == SyntaxKind.StyleRule && context == BlockContext.TopLevel)
        {
            _topLevelState = TopLevelState.Rules;
        }

        var block = ParseBlock(GetQualifiedRuleBlockContext(kind));

        return Finish(new CssQualifiedRuleSyntax(kind, preludeNode, block), _starts[start], mark);
    }

    private bool LooksLikeCustomProperty(int start, int end)
        => end - start >= 2 && KindAt(start) == SyntaxKind.IdentToken && SyntaxFacts.IsCustomPropertyName(_tokens[start].ValueText) && KindAt(start + 1) == SyntaxKind.ColonToken;

    private static SyntaxKind GetQualifiedRuleKind(BlockContext context) => context switch
    {
        BlockContext.TopLevel or BlockContext.Group or BlockContext.Scope or BlockContext.StyleRule => SyntaxKind.StyleRule,
        BlockContext.Keyframes => SyntaxKind.KeyframeRule,
        _ => SyntaxKind.QualifiedRule,
    };

    private static BlockContext GetQualifiedRuleBlockContext(SyntaxKind kind) => kind switch
    {
        SyntaxKind.StyleRule => BlockContext.StyleRule,
        SyntaxKind.KeyframeRule => BlockContext.DeclarationsOnly,
        _ => BlockContext.Unknown,
    };

    private GreenNode? ParseQualifiedRulePrelude(SyntaxKind kind, BlockContext context, int end)
    {
        switch (kind)
        {
            case SyntaxKind.StyleRule:
                var relative = context is BlockContext.StyleRule or BlockContext.Scope;
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.ExpectedSelector, () => ParseSelectorList(relative ? SelectorListMode.Relative : SelectorListMode.Complex), required: true);

            case SyntaxKind.KeyframeRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.ExpectedKeyframeSelector, ParseKeyframeSelectorList, required: true);

            default:
                return _index == end ? null : ParseGenericPrelude(end);
        }
    }

    /// <summary>Parses a <c>{}</c> block whose opening brace is the current token, holding the statements <paramref name="context"/> allows.</summary>
    private CssBlockSyntax ParseBlock(BlockContext context)
    {
        var openIndex = _index;
        var open = EatToken();
        var close = EndOf(openIndex);
        if (IsTooDeep())
        {
            // The rest of the block is kept as it is, and reported once.
            var mark = _pending.Count;
            AddError(openIndex, CssDiagnosticDescriptors.NestingTooDeep, _depth);
            var skipped = new List<GreenNode?>();
            while (_index < close)
            {
                skipped.Add(SyntaxFactory.AsSkippedText(EatToken()));
            }

            var statements = skipped.Count == 0 ? null : SyntaxFactory.List((ReadOnlySpan<GreenNode?>)[new CssSkippedTextSyntax(SyntaxFactory.TokenList(skipped))]);
            var closeToken = EatCloser(close, SyntaxKind.CloseBraceToken);
            return (CssBlockSyntax)Finish(new CssBlockSyntax(open, statements, closeToken), _starts[openIndex], mark);
        }

        var savedLimit = _limit;
        _limit = close;
        _depth++;
        var content = ParseStatements(context, isTopLevel: false);
        _depth--;
        _limit = savedLimit;

        var closeBrace = EatCloser(close, SyntaxKind.CloseBraceToken);
        return new CssBlockSyntax(open, SyntaxFactory.List(content), closeBrace);
    }

    /// <summary>Eats the closing token at <paramref name="close"/>, or reports it missing when the block runs to the end of its range.</summary>
    private GreenToken EatCloser(int close, SyntaxKind kind)
    {
        if (close < _limit && KindAt(close) == kind && _index == close)
            return EatToken();

        AddMissingToken(CssDiagnosticDescriptors.ExpectedToken, SyntaxFacts.GetText(kind));
        return SyntaxFactory.MissingToken(kind);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Component values.
    // ------------------------------------------------------------------------------------------------------------

    private List<GreenNode?> ParseComponentValues(int end)
    {
        var savedLimit = _limit;
        _limit = Math.Min(end, _limit);
        var values = new List<GreenNode?>();
        while (!AtEnd)
        {
            values.Add(ParseComponentValue());
        }

        _limit = savedLimit;
        return values;
    }

    private GreenNode ParseComponentValue()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.FunctionToken:
            case SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken:
                if (IsTooDeep())
                    return ParseTooDeepValue();

                var openIndex = _index;
                var open = EatToken();
                var close = EndOf(openIndex);
                _depth++;
                var values = ParseComponentValues(close);
                _depth--;
                var closeKind = SyntaxFacts.GetClosingKind((SyntaxKind)open.RawKind);
                var mark = _pending.Count;
                var closeToken = EatCloser(close, closeKind);
                GreenNode node = open.RawKind == (int)SyntaxKind.FunctionToken
                    ? new CssFunctionSyntax(open, SyntaxFactory.List(values), closeToken)
                    : new CssSimpleBlockSyntax(GetSimpleBlockKind((SyntaxKind)open.RawKind), open, SyntaxFactory.List(values), closeToken);
                return Finish(node, _starts[openIndex], mark);

            default:
                return new CssTokenValueSyntax(EatToken());
        }
    }

    private static SyntaxKind GetSimpleBlockKind(SyntaxKind open) => open switch
    {
        SyntaxKind.OpenParenToken => SyntaxKind.ParenthesizedBlock,
        SyntaxKind.OpenBracketToken => SyntaxKind.BracketedBlock,
        _ => SyntaxKind.BracedBlock,
    };

    /// <summary>Keeps a function or block nested beyond the limit, and everything in it, as one skipped value.</summary>
    private CssSkippedValueSyntax ParseTooDeepValue()
    {
        var start = _index;
        var mark = _pending.Count;
        AddError(_index, CssDiagnosticDescriptors.NestingTooDeep, _depth);
        var end = SkipComponentValue(_index);
        var tokens = new List<GreenNode?>();
        while (_index < end)
        {
            tokens.Add(SyntaxFactory.AsSkippedText(EatToken()));
        }

        return (CssSkippedValueSyntax)Finish(new CssSkippedValueSyntax(SyntaxFactory.TokenList(tokens)), _starts[start], mark);
    }

    /// <summary>Parses the tokens up to <paramref name="end"/> as component values, or returns <see langword="null"/> when there are none.</summary>
    private CssGenericPreludeSyntax? ParseGenericPrelude(int end)
    {
        var values = ParseComponentValues(end);
        return values.Count == 0 ? null : new CssGenericPreludeSyntax(SyntaxFactory.List(values));
    }

    /// <summary>Parses a prelude with its grammar, falling back to component values when the tokens do not match it.</summary>
    /// <param name="end">Where the prelude ends.</param>
    /// <param name="emptyDescriptor">What to report when the prelude is empty and <paramref name="required"/> is set.</param>
    /// <param name="parse">Parses the prelude, returning <see langword="null"/> after recording a failure when it does not match.</param>
    /// <param name="required">Whether an empty prelude is an error.</param>
    /// <param name="emptyArguments">The arguments of <paramref name="emptyDescriptor"/>.</param>
    private GreenNode? ParseTypedPrelude(int end, DiagnosticDescriptor emptyDescriptor, Func<GreenNode?> parse, bool required, params object?[] emptyArguments)
    {
        var start = _index;
        var mark = _pending.Count;
        if (start == end)
        {
            if (required)
            {
                AddMissingToken(emptyDescriptor, emptyArguments);
            }

            return null;
        }

        var savedLimit = _limit;
        _limit = end;
        var point = Mark();
        _failure = null;
        var node = parse();
        if (node is not null && _index == end)
        {
            _limit = savedLimit;
            _failure = point.Failure;
            return Finish(node, _starts[start], mark);
        }

        var failure = _failure ?? (_index < end
            ? new PendingDiagnostic(TextStart(_index), _tokens[_index].Text.Length, CssDiagnosticDescriptors.UnexpectedToken, [_tokens[_index].Text])
            : new PendingDiagnostic(TextStart(start), 0, emptyDescriptor, emptyArguments));
        Reset(point);
        _limit = savedLimit;

        // A browser drops the whole rule: the prelude is kept as plain values, with the reason it does not match.
        MarkSkipped(start);
        var generic = ParseGenericPrelude(end);
        if (failure.Descriptor != CssDiagnosticDescriptors.NestingTooDeep)
        {
            _pending.Add(failure);
        }

        return generic is null ? null : Finish(generic, _starts[start], mark);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Tokens.
    // ------------------------------------------------------------------------------------------------------------

    private SyntaxKind KindAt(int index) => index < _limit ? (SyntaxKind)_tokens[index].RawKind : SyntaxKind.EndOfFileToken;

    private GreenToken EatToken()
    {
        Debug.Assert(_index < _limit, "Eating past the end of the range.");
        return _tokens[_index++];
    }

    /// <summary>Gets the index of the token after the component value at <paramref name="index"/>.</summary>
    private int SkipComponentValue(int index)
    {
        if (SyntaxFacts.GetClosingKind(KindAt(index)) == SyntaxKind.None)
            return index + 1;

        var close = EndOf(index);
        return close < _limit ? close + 1 : _limit;
    }

    /// <summary>Gets the index of the token that closes the opening token at <paramref name="openIndex"/>, or the end of the range when nothing does.</summary>
    private int EndOf(int openIndex)
    {
        var close = _match[openIndex];
        return close < 0 || close > _limit ? _limit : close;
    }

    /// <summary>Determines whether whitespace separates the token at <paramref name="index"/> from the one before it. A comment alone does not.</summary>
    private bool HasWhitespaceBefore(int index)
        => index > 0 && (ContainsWhitespace(_tokens[index - 1].TrailingTrivia) || ContainsWhitespace(_tokens[index].LeadingTrivia));

    private static bool ContainsWhitespace(GreenNode? trivia)
    {
        if (trivia is null)
            return false;

        var count = GreenNodeList.Count(trivia);
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(trivia, i)?.RawKind is (int)SyntaxKind.WhitespaceTrivia or (int)SyntaxKind.EndOfLineTrivia)
                return true;
        }

        return false;
    }

    private int TextStart(int index) => _starts[index] + _tokens[index].GetLeadingTriviaWidth();

    private int TextEnd(int index) => TextStart(index) + _tokens[index].Text.Length;

    /// <summary>Gets the text of the tokens from <paramref name="start"/> up to <paramref name="end"/>, without the trivia around them.</summary>
    private string TextOf(int start, int end)
    {
        if (start >= end)
            return "";

        var builder = new System.Text.StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (i > start)
            {
                builder.Append(_tokens[i].LeadingTrivia?.ToFullString());
            }

            builder.Append(_tokens[i].Text);
            if (i < end - 1)
            {
                builder.Append(_tokens[i].TrailingTrivia?.ToFullString());
            }
        }

        return builder.ToString();
    }

    /// <summary>Marks the first token of a construct a browser drops, so that every node above it reports skipped text.</summary>
    private void MarkSkipped(int index)
    {
        if (index < _limit)
        {
            _tokens[index] = SyntaxFactory.AsSkippedText(_tokens[index]);
        }
    }

    private bool IsTooDeep() => _depth >= _options.MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack();

    private static int[] MatchBrackets(GreenToken[] tokens)
    {
        // Only the closing token of the innermost open block closes anything; any other one is an ordinary token, as
        // it is for "consume a simple block" and "consume a function".
        var match = new int[tokens.Length];
        Array.Fill(match, -1);
        var open = new Stack<int>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var kind = (SyntaxKind)tokens[i].RawKind;
            if (SyntaxFacts.GetClosingKind(kind) != SyntaxKind.None)
            {
                open.Push(i);
            }
            else if (kind is SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken
                && open.Count > 0 && SyntaxFacts.GetClosingKind((SyntaxKind)tokens[open.Peek()].RawKind) == kind)
            {
                var openIndex = open.Pop();
                match[openIndex] = i;
                match[i] = openIndex;
            }
        }

        return match;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Speculation.
    // ------------------------------------------------------------------------------------------------------------

    private ResetPoint Mark() => new(_index, _limit, _depth, _pending.Count, _failure);

    private void Reset(ResetPoint point)
    {
        _index = point.Index;
        _limit = point.Limit;
        _depth = point.Depth;
        _pending.RemoveRange(point.PendingCount, _pending.Count - point.PendingCount);
        _failure = point.Failure;
    }

    /// <summary>Records why the grammar being tried does not match, keeping the first reason, and returns <see langword="null"/>.</summary>
    private T? Fail<T>(int index, DiagnosticDescriptor descriptor, params object?[] arguments)
        where T : class
    {
        if (_failure is null)
        {
            if (index < _limit)
            {
                _failure = new PendingDiagnostic(TextStart(index), _tokens[index].Text.Length, descriptor, arguments);
            }
            else
            {
                var position = index > 0 ? TextEnd(Math.Min(index, _tokens.Length) - 1) : 0;
                _failure = new PendingDiagnostic(position, 0, descriptor, arguments);
            }
        }

        return null;
    }

    private readonly record struct ResetPoint(int Index, int Limit, int Depth, int PendingCount, PendingDiagnostic? Failure);

    // ------------------------------------------------------------------------------------------------------------
    // Diagnostics.
    // ------------------------------------------------------------------------------------------------------------

    private void AddError(int index, DiagnosticDescriptor descriptor, params object?[] arguments)
        => _pending.Add(new PendingDiagnostic(TextStart(index), _tokens[index].Text.Length, descriptor, arguments));

    /// <summary>Records a diagnostic spanning the text of the tokens from <paramref name="start"/> up to <paramref name="end"/>.</summary>
    private void AddErrorSpan(int start, int end, DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        if (start >= end)
        {
            AddMissingToken(descriptor, arguments);
            return;
        }

        _pending.Add(new PendingDiagnostic(TextStart(start), TextEnd(end - 1) - TextStart(start), descriptor, arguments));
    }

    /// <summary>Records an error for something the text does not have, at the end of the token before it.</summary>
    private void AddMissingToken(DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        var position = _index > 0 ? TextEnd(_index - 1) : TextStart(_index);
        _pending.Add(new PendingDiagnostic(position, 0, descriptor, arguments));
    }

    private GreenNode Finish(GreenNode node, int nodeFullStart, int mark)
    {
        if (_pending.Count == mark)
            return node;

        var infos = new SyntaxDiagnosticInfo[_pending.Count - mark];
        for (var i = 0; i < infos.Length; i++)
        {
            var pending = _pending[mark + i];
            infos[i] = new SyntaxDiagnosticInfo(Math.Max(0, pending.Position - nodeFullStart), pending.Width, pending.Descriptor, pending.Arguments);
        }

        _pending.RemoveRange(mark, infos.Length);

        return node.WithAdditionalDiagnostics(infos);
    }

    private readonly record struct PendingDiagnostic(int Position, int Width, DiagnosticDescriptor Descriptor, object?[] Arguments);
}
