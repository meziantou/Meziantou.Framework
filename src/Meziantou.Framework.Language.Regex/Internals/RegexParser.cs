using System.Globalization;

namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The grammar every flavor shares: alternation, sequence, and quantifier.</summary>
/// <remarks>
/// <para>
/// The engine this is modelled on scans a run of ordinary characters and then splits the last one off if a quantifier
/// turns out to follow. That cannot produce exact spans: in extended mode the run is interrupted by trivia, so the
/// characters it covers are not contiguous in the source. Here every literal is one atom of one UTF-16 code unit, a
/// quantifier binds the atom node in front of it, and trivia is peeked before it is claimed. One code unit rather than
/// one rune is deliberate and matches the engine: in <c>"😀*"</c> the quantifier applies to the low surrogate.
/// </para>
/// <para>
/// A quantifier applied to a quantifier is recovery rather than a fatal error, which is also what lets the same code
/// read <c>a*+</c> as a possessive quantifier for the flavors that have them.
/// </para>
/// </remarks>
internal abstract class RegexParser
{
    private readonly List<RegexDiagnostic> _diagnostics = [];
    private readonly Dictionary<int, TextSpan> _captureSpans = [];
    private int _depth;

    protected RegexParser(string text, RegexParseOptions parseOptions)
    {
        Text = text;
        ParseOptions = parseOptions;
        Options = parseOptions.PatternOptions;
        Scanner = new RegexScanner(text, _diagnostics);
        CaptureTable = RegexCaptureTable.Empty;
    }

    protected string Text { get; }
    protected RegexScanner Scanner { get; }
    protected RegexParseOptions ParseOptions { get; }
    protected RegexFlavor Flavor => ParseOptions.Flavor;

    /// <summary>The options in effect at the reading position.</summary>
    protected RegexPatternOptions Options { get; set; }

    /// <summary>The options saved at each open parenthesis, restored at the matching close.</summary>
    protected Stack<RegexPatternOptions> OptionsStack { get; } = new();

    protected RegexCaptureTable CaptureTable { get; set; }

    public IReadOnlyList<RegexDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<RegexCaptureInfo> Captures { get; private set; } = [];

    /// <summary>The builder that collects capture slots, non-null only during the numbering pass.</summary>
    protected RegexCaptureTable.Builder? CaptureBuilder { get; private set; }

    /// <summary>
    /// Walks the pattern once and reports the capture groups it declares, discarding everything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whether <c>\1</c> is a backreference or an octal escape, whether <c>(?(1)…)</c> tests a group or matches an
    /// expression, and what number a named group ends up with all depend on groups that may be declared later, so the
    /// numbering has to be known before the first atom is classified.
    /// </para>
    /// <para>
    /// The pass is the real parser rather than a cut-down skipper. A skipper would have to agree with the parser on
    /// exactly how many characters every escape and every character class covers -- <c>[\x5D]</c> alone is enough to
    /// break one that does not -- and any disagreement would show up as a miscounted group somewhere far away. Running
    /// the same code twice cannot disagree with itself, and a pattern is short enough that the second walk costs
    /// nothing worth saving.
    /// </para>
    /// </remarks>
    public RegexCaptureTable CollectCaptureTable()
    {
        CaptureBuilder = new RegexCaptureTable.Builder();
        CaptureBuilder.NoteSlot(0, 0);
        ParseRoot();

        return CaptureBuilder.Build();
    }

    /// <summary>Parses the whole pattern. Never throws; problems become diagnostics.</summary>
    public RegexPatternSyntax ParsePattern(RegexCaptureTable captureTable)
    {
        CaptureTable = captureTable;
        var root = ParseRoot();
        Captures = BuildCaptures();

        return root;
    }

    private RegexPatternSyntax ParseRoot()
    {
        var openSlashToken = ReadLiteralPrefix();
        var alternation = ParseAlternation(insideGroup: false);
        var (closeSlashToken, flagsToken, trailingToken) = ReadLiteralSuffix();

        var trivia = TakeTrivia();
        var endOfPatternToken = Scanner.MissingToken(RegexSyntaxKind.EndOfPatternToken, trivia);

        return new RegexPatternSyntax(alternation, endOfPatternToken, openSlashToken, closeSlashToken, flagsToken, trailingToken, Text)
        {
            Options = ParseOptions.PatternOptions,
        };
    }

    /// <summary>Reads the opening delimiter of a JavaScript literal. Every other flavor has none.</summary>
    protected virtual RegexSyntaxToken? ReadLiteralPrefix() => null;

    /// <summary>Reads the closing delimiter, flags, and any trailing content of a JavaScript literal.</summary>
    protected virtual (RegexSyntaxToken? CloseSlash, RegexSyntaxToken? Flags, RegexSyntaxToken? Trailing) ReadLiteralSuffix() => (null, null, null);

    /// <summary>
    /// Returns whether the pattern body ends at <paramref name="position"/>. A JavaScript literal ends at its closing
    /// delimiter rather than at the end of the text.
    /// </summary>
    protected virtual bool IsAtBodyEnd(int position) => position >= Text.Length;

    /// <summary>Parses one atom. Must always consume at least one character, so the parser cannot loop forever.</summary>
    protected abstract RegexAtomSyntax ParseAtom(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia);

    /// <summary>Parses the branches of an alternation, in order.</summary>
    protected RegexAlternationSyntax ParseAlternation(bool insideGroup)
    {
        var start = Scanner.Position;
        var branches = new List<RegexSequenceSyntax>();
        var barTokens = new List<RegexSyntaxToken>();
        var supportsAlternation = Flavor.HasFeature(RegexFlavorFeatures.Alternation);

        while (true)
        {
            branches.Add(ParseSequence(insideGroup));

            var barPosition = PeekTriviaEnd();
            if (!supportsAlternation || IsAtBodyEnd(barPosition) || Scanner.CharAt(barPosition) != '|')
                break;

            var trivia = TakeTrivia();
            var barStart = Scanner.Position;
            Scanner.Position++;
            barTokens.Add(Scanner.Token(RegexSyntaxKind.BarToken, barStart, trivia));
        }

        return WithOptions(new RegexAlternationSyntax(branches, barTokens, start));
    }

    /// <summary>Parses one branch: the terms that must match one after another.</summary>
    /// <remarks>
    /// The trivia in front of whatever comes next is peeked rather than claimed, because it may belong to the caller's
    /// <c>)</c> or <c>|</c> rather than to a term of this branch.
    /// </remarks>
    protected RegexSequenceSyntax ParseSequence(bool insideGroup)
    {
        var start = Scanner.Position;
        var terms = new List<RegexTermSyntax>();
        var supportsAlternation = Flavor.HasFeature(RegexFlavorFeatures.Alternation);

        while (true)
        {
            var triviaEnd = PeekTriviaEnd();
            if (IsAtBodyEnd(triviaEnd))
                break;

            var next = Scanner.CharAt(triviaEnd);
            if (supportsAlternation && next == '|')
                break;

            if (insideGroup && next == ')')
                break;

            var before = Scanner.Position;
            var atom = ParseAtom(TakeTrivia());

            // An inline option setter matches nothing, so a quantifier after it has nothing to repeat. Leaving the
            // quantifier for the next turn is what reports it, because an atom position is where that is diagnosed.
            terms.Add(atom is RegexInlineOptionsSyntax ? atom : ParseQuantifiers(atom));

            if (Scanner.Position <= before)
            {
                // A parser that failed to consume anything would spin forever. Nothing should reach this, but the
                // guarantee that parsing always terminates is worth more than the branch it costs.
                Scanner.Position = before + 1;
            }
        }

        return WithOptions(new RegexSequenceSyntax(terms, start));
    }

    /// <summary>Applies every quantifier that follows <paramref name="atom"/>, innermost first.</summary>
    protected RegexTermSyntax ParseQuantifiers(RegexAtomSyntax atom)
    {
        RegexTermSyntax term = atom;

        while (true)
        {
            var triviaEnd = PeekTriviaEnd();
            if (IsAtBodyEnd(triviaEnd) || !IsQuantifierAt(triviaEnd))
                return term;

            var trivia = TakeTrivia();
            var quantifier = ParseQuantifier(trivia);
            if (quantifier is null)
                return term;

            if (term is RegexQuantifiedSyntax)
            {
                Scanner.AddDiagnostic(
                    quantifier.Span,
                    RegexDiagnosticIds.NestedQuantifiersNotParenthesized,
                    $"Nested quantifier '{quantifier.ToString().Trim()}' is not enclosed in parentheses.");
            }

            term = WithOptions(new RegexQuantifiedSyntax(term, quantifier));
        }
    }

    /// <summary>Returns whether a quantifier starts at <paramref name="position"/>.</summary>
    protected virtual bool IsQuantifierAt(int position)
    {
        if (position >= Text.Length)
            return false;

        var ch = Text[position];
        if (ch is '+' or '?')
            return Flavor.HasFeature(RegexFlavorFeatures.PlusAndQuestionQuantifiers);

        if (ch == '{')
            return !Flavor.HasFeature(RegexFlavorFeatures.EscapedGroupDelimiters) && RegexCharacterTables.IsTrueQuantifier(Text, position);

        return ch == '*';
    }

    private RegexQuantifierSyntax? ParseQuantifier(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;
        var ch = Scanner.Current;

        if (ch is '*' or '+' or '?')
        {
            Scanner.Position++;
            var operatorToken = Scanner.Token(QuantifierTokenKind(ch), start, leadingTrivia);

            return new RegexSimpleQuantifierSyntax(operatorToken, ReadQuantifierModifier());
        }

        return ParseRangeQuantifier(leadingTrivia);
    }

    private static RegexSyntaxKind QuantifierTokenKind(char ch) => ch switch
    {
        '*' => RegexSyntaxKind.AsteriskToken,
        '+' => RegexSyntaxKind.PlusToken,
        _ => RegexSyntaxKind.QuestionToken,
    };

    /// <summary>Reads a <c>{n}</c>, <c>{n,}</c>, or <c>{n,m}</c> bound.</summary>
    /// <remarks>
    /// The caller only gets here once the look-ahead has confirmed the bound is well formed, so every token below is
    /// present. A bound that does not fit in an <see cref="int"/> is still consumed in full, so the span stays exact,
    /// and the clamped value is carried on the token so a consumer does not have to reparse the digits.
    /// </remarks>
    private RegexRangeQuantifierSyntax ParseRangeQuantifier(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var braceStart = Scanner.Position;
        Scanner.Position++;
        var openBraceToken = Scanner.Token(RegexSyntaxKind.OpenBraceToken, braceStart, leadingTrivia);

        var minToken = ReadBound();
        RegexSyntaxToken? commaToken = null;
        RegexSyntaxToken? maxToken = null;
        if (Scanner.Current == ',')
        {
            var commaStart = Scanner.Position;
            Scanner.Position++;
            commaToken = Scanner.Token(RegexSyntaxKind.CommaToken, commaStart);
            if (Scanner.Current != '}')
            {
                maxToken = ReadBound();
            }
        }

        var closeStart = Scanner.Position;
        RegexSyntaxToken closeBraceToken;
        if (Scanner.Current == '}')
        {
            Scanner.Position++;
            closeBraceToken = Scanner.Token(RegexSyntaxKind.CloseBraceToken, closeStart);
        }
        else
        {
            closeBraceToken = Scanner.MissingToken(RegexSyntaxKind.CloseBraceToken);
        }

        var quantifier = new RegexRangeQuantifierSyntax(openBraceToken, minToken, commaToken, maxToken, closeBraceToken, ReadQuantifierModifier());
        if (quantifier.MaxCount is { } max && quantifier.MinCount > max)
        {
            Scanner.AddDiagnostic(
                quantifier.Span,
                RegexDiagnosticIds.ReversedQuantifierRange,
                FormattableString.Invariant($"Quantifier range {quantifier.MinCount},{max} is reversed."));
        }

        return quantifier;
    }

    private RegexSyntaxToken ReadBound()
    {
        var start = Scanner.Position;
        var overflowed = false;
        long value = 0;
        while (char.IsAsciiDigit(Scanner.Current))
        {
            if (!overflowed)
            {
                value = (value * 10) + (Scanner.Current - '0');
                if (value > int.MaxValue)
                {
                    overflowed = true;
                }
            }

            Scanner.Position++;
        }

        if (overflowed)
        {
            Scanner.AddDiagnostic(
                TextSpan.FromBounds(start, Scanner.Position),
                RegexDiagnosticIds.QuantifierOrCaptureGroupOutOfRange,
                "The quantifier or capture group number is larger than Int32.MaxValue.");
            value = int.MaxValue;
        }

        return Scanner.Token(RegexSyntaxKind.NumberToken, start, leadingTrivia: null, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Reads the <c>?</c> or <c>+</c> that makes a quantifier lazy or possessive.</summary>
    /// <remarks>
    /// The engine scans trivia between the operator and the <c>?</c>, so <c>a{2,3} ?</c> is lazy in extended mode. The
    /// trivia becomes the modifier's leading trivia, which is where it round-trips from.
    /// </remarks>
    private RegexSyntaxToken? ReadQuantifierModifier()
    {
        var triviaEnd = PeekTriviaEnd();
        var next = Scanner.CharAt(triviaEnd);

        var kind = next switch
        {
            '?' when Flavor.HasFeature(RegexFlavorFeatures.LazyQuantifiers) => RegexSyntaxKind.QuestionToken,
            '+' when Flavor.HasFeature(RegexFlavorFeatures.PossessiveQuantifiers) => RegexSyntaxKind.PlusToken,
            _ => RegexSyntaxKind.None,
        };

        if (kind == RegexSyntaxKind.None)
            return null;

        var trivia = TakeTrivia();
        var start = Scanner.Position;
        Scanner.Position++;

        return Scanner.Token(kind, start, trivia);
    }

    // ---- helpers shared by the flavor parsers ----

    /// <summary>
    /// Whether the pattern is read with ECMAScript behaviour: <c>[^]</c> is an empty negated class, octal escapes stop
    /// early, and a numeric backreference takes the longest prefix that names an existing group.
    /// </summary>
    /// <remarks>
    /// It is either asked for through .NET's own ECMAScript option or implied by the flavor, because for JavaScript
    /// those are simply the rules rather than an option.
    /// </remarks>
    protected bool UsesEcmaScriptBehavior =>
        (Options & RegexPatternOptions.EcmaScript) != RegexPatternOptions.None ||
        Flavor.Family == RegexFlavorFamily.JavaScript;

    /// <summary>Whether the pattern is read as a sequence of code points rather than of UTF-16 code units.</summary>
    protected bool UsesUnicodeMode => (Options & RegexPatternOptions.Unicode) != RegexPatternOptions.None;

    protected int PeekTriviaEnd() => Scanner.PeekTriviaEnd(Options, Flavor);

    protected IReadOnlyList<RegexSyntaxTrivia> TakeTrivia() => Scanner.TakeTrivia(Options, Flavor);

    /// <summary>Stamps the options in effect onto a node as it is built.</summary>
    protected TNode WithOptions<TNode>(TNode node)
        where TNode : RegexSyntaxNode
    {
        node.Options = Options;

        return node;
    }

    protected void AddDiagnostic(TextSpan span, string id, string message) => Scanner.AddDiagnostic(span, id, message);

    protected void AddDiagnostic(int start, string id, string message) =>
        Scanner.AddDiagnostic(TextSpan.FromBounds(start, Math.Max(start, Scanner.Position)), id, message);

    /// <summary>Takes the next capture number, noting the slot when this is the numbering pass.</summary>
    protected int NoteAutoCapture(int position) => CaptureBuilder?.NoteAutoSlot(position) ?? NextAutoCapture();

    /// <summary>Takes the next capture number without noting it.</summary>
    protected abstract int NextAutoCapture();

    /// <summary>Notes an explicitly numbered group, as <c>(?&lt;3&gt;x)</c> declares.</summary>
    protected void NoteCaptureNumber(int number, int position) => CaptureBuilder?.NoteSlot(number, position);

    /// <summary>Notes a named group.</summary>
    protected void NoteCaptureName(string name, int position) => CaptureBuilder?.NoteName(name, position);

    /// <summary>Records where a capture group turned out to be, so the tree and the numbering agree.</summary>
    protected void NoteCaptureSpan(int number, TextSpan span)
    {
        if (number > 0)
        {
            _captureSpans[number] = span;
        }
    }

    /// <summary>Enters a nested construct, or reports that the pattern nests too deeply.</summary>
    protected bool TryEnterRecursion(TextSpan span)
    {
        if (_depth >= ParseOptions.MaxRecursionDepth)
        {
            AddDiagnostic(span, RegexDiagnosticIds.MaxRecursionDepthExceeded, "The pattern nests more deeply than the configured maximum.");

            return false;
        }

        _depth++;

        return true;
    }

    protected void ExitRecursion() => _depth--;

    /// <summary>Folds everything that is left into one skipped-text atom so the pattern still round-trips.</summary>
    protected RegexSkippedTextSyntax ConsumeRestAsText(int start, IReadOnlyList<RegexSyntaxTrivia>? leadingTrivia = null)
    {
        Scanner.Position = Text.Length;

        return WithOptions(new RegexSkippedTextSyntax([Scanner.Token(RegexSyntaxKind.BadToken, start, leadingTrivia)], start));
    }

    /// <summary>Builds a one-character skipped-text atom for input the grammar has no place for.</summary>
    protected RegexSkippedTextSyntax SkipOneCharacter(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia, string id, string message)
    {
        var start = Scanner.Position;
        Scanner.Position++;
        var token = Scanner.Token(RegexSyntaxKind.BadToken, start, leadingTrivia);
        AddDiagnostic(TextSpan.FromBounds(start, Scanner.Position), id, message);

        return WithOptions(new RegexSkippedTextSyntax([token], token.FullSpan.Start));
    }

    private List<RegexCaptureInfo> BuildCaptures()
    {
        if (CaptureTable.Numbers.Count == 0)
            return [];

        var captures = new List<RegexCaptureInfo>(CaptureTable.Numbers.Count);
        foreach (var number in CaptureTable.Numbers)
        {
            var span = _captureSpans.TryGetValue(number, out var recorded)
                ? recorded
                : new TextSpan(CaptureTable.GetPosition(number), 0);

            captures.Add(new RegexCaptureInfo(number, CaptureTable.GetName(number), span));
        }

        return captures;
    }
}
