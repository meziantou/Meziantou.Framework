using System.Globalization;

using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using ScannedToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>The grammar every dialect shares: alternation, sequence, and quantifier.</summary>
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
/// read <c>a*+</c> as a possessive quantifier for the dialects that have them.
/// </para>
/// </remarks>
internal abstract class RegexParser
{
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Dictionary<int, TextSpan> _captureSpans = [];
    private readonly List<(int Alternation, int Branch)> _alternativePath = [];
    private readonly List<int> _completedCaptures = [];
    private int _nextAlternationId;
    private int _depth;

    protected RegexParser(SourceText source, RegexParseOptions parseOptions)
    {
        Source = source;
        Text = source.Text;
        ParseOptions = parseOptions;
        Options = parseOptions.PatternOptions;
        Scanner = new RegexScanner(source, _diagnostics);
        CaptureTable = RegexCaptureTable.Empty;
    }

    protected SourceText Source { get; }
    protected string Text { get; }
    protected RegexScanner Scanner { get; }
    protected RegexParseOptions ParseOptions { get; }
    protected RegexDialect Dialect => ParseOptions.Dialect;

    /// <summary>The options in effect at the reading position.</summary>
    protected RegexPatternOptions Options { get; set; }

    /// <summary>
    /// The options saved at each open parenthesis, restored at the matching close, along with whether duplicate group
    /// names were allowed, which PCRE scopes the same way.
    /// </summary>
    protected Stack<(RegexPatternOptions Options, bool DuplicateNamesAllowed)> OptionsStack { get; } = new();

    /// <summary>Whether a group name may be declared again, as PCRE's <c>(?J)</c> allows, at the reading position.</summary>
    protected bool DuplicateNamesAllowed { get; set; }

    protected RegexCaptureTable CaptureTable { get; set; }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

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
        OnPatternParsed();
        Captures = BuildCaptures();

        return root;
    }

    private RegexPatternSyntax ParseRoot()
    {
        var openSlashToken = ReadLiteralPrefix();
        var alternation = ParseAlternation(insideGroup: false);
        var (closeSlashToken, flagsToken, trailingToken) = ReadLiteralSuffix();

        var trivia = TakeTrivia();
        var endOfPatternToken = Scanner.MissingToken(SyntaxKind.EndOfPatternToken, trivia);

        return new RegexPatternSyntax(openSlashToken, alternation, closeSlashToken, flagsToken, trailingToken, endOfPatternToken, ParseOptions.PatternOptions);
    }

    /// <summary>
    /// The range a node just read from the pattern covers, worked out from where the scanner now stands.
    /// </summary>
    /// <remarks>
    /// Only correct immediately after the node was parsed and before anything else is read, because it measures back
    /// from the reading position rather than remembering where the node began.
    /// </remarks>
    protected TextSpan JustParsedSpan(GreenNode node) => TextSpan.FromBounds(Math.Max(0, Scanner.Position - node.Width), Scanner.Position);

    /// <summary>Weaves nodes and the separators that follow them into the one sequence a separated list holds.</summary>
    protected static GreenNode? Interleave<TNode>(List<TNode> nodes, List<ScannedToken> separators)
        where TNode : RegexSyntaxNode
    {
        var items = new List<GreenNode?>((nodes.Count * 2) - 1);
        for (var index = 0; index < nodes.Count; index++)
        {
            items.Add(nodes[index]);
            if (index < separators.Count)
            {
                items.Add(separators[index]);
            }
        }

        for (var index = nodes.Count; index < separators.Count; index++)
        {
            items.Add(separators[index]);
        }

        return SyntaxFactory.ListNode([.. items]);
    }

    /// <summary>Called once the whole pattern has been read, for checks that need groups declared after the point they concern.</summary>
    private protected virtual void OnPatternParsed()
    {
    }

    /// <summary>Reads the opening delimiter of a JavaScript literal. Every other dialect has none.</summary>
    protected virtual ScannedToken ReadLiteralPrefix() => default;

    /// <summary>Reads the closing delimiter, flags, and any trailing content of a JavaScript literal.</summary>
    protected virtual (ScannedToken CloseSlash, ScannedToken Flags, ScannedToken Trailing) ReadLiteralSuffix() => (default, default, default);

    /// <summary>
    /// Returns whether the pattern body ends at <paramref name="position"/>. A JavaScript literal ends at its closing
    /// delimiter rather than at the end of the text.
    /// </summary>
    protected virtual bool IsAtBodyEnd(int position) => position >= Text.Length;

    /// <summary>Parses one atom. Must always consume at least one character, so the parser cannot loop forever.</summary>
    protected abstract RegexAtomSyntax ParseAtom(GreenNode? leadingTrivia);

    // The delimiters below are virtual because a POSIX basic expression spells them with a backslash: "\(" opens a
    // group and a bare "(" is a character, which is the reverse of every other dialect. Everything that reads a
    // delimiter goes through these, so the grammar skeleton itself does not care which spelling is in use.

    /// <summary>The length of the alternation separator at <paramref name="position"/>, or 0 when there is none.</summary>
    protected virtual int AlternationSeparatorLength(int position) =>
        position < Text.Length && Text[position] == '|' ? 1 : 0;

    /// <summary>The length of the token that opens a group at <paramref name="position"/>, or 0.</summary>
    protected virtual int GroupOpenLength(int position) =>
        position < Text.Length && Text[position] == '(' ? 1 : 0;

    /// <summary>The length of the token that closes a group at <paramref name="position"/>, or 0.</summary>
    protected virtual int GroupCloseLength(int position) =>
        position < Text.Length && Text[position] == ')' ? 1 : 0;

    /// <summary>The length of the token that opens a bound at <paramref name="position"/>, or 0.</summary>
    protected virtual int BoundOpenLength(int position) =>
        position < Text.Length && Text[position] == '{' ? 1 : 0;

    /// <summary>The length of the token that closes a bound at <paramref name="position"/>, or 0.</summary>
    protected virtual int BoundCloseLength(int position) =>
        position < Text.Length && Text[position] == '}' ? 1 : 0;

    /// <summary>
    /// The length of a <c>*</c>, <c>+</c>, or <c>?</c> quantifier at <paramref name="position"/>, or 0, reporting
    /// which operator it is. GNU basic expressions spell two of the three escaped.
    /// </summary>
    protected virtual int SimpleQuantifierLength(int position, out char operatorCharacter)
    {
        operatorCharacter = position < Text.Length ? Text[position] : '\0';

        return operatorCharacter is '*' or '+' or '?' ? 1 : 0;
    }

    /// <summary>Whether the reading position is at the start of a branch, where POSIX changes what is special.</summary>
    protected bool IsAtSequenceStart { get; private set; }

    /// <summary>Parses the branches of an alternation, in order.</summary>
    /// <param name="insideGroup">Whether a <c>)</c> ends the alternation.</param>
    /// <param name="resetsCaptureNumbers">
    /// Whether every branch numbers its groups from the same starting point, as a PCRE branch reset group does. The
    /// groups after the alternation then continue from the highest number any branch reached.
    /// </param>
    protected RegexAlternationSyntax ParseAlternation(bool insideGroup, bool resetsCaptureNumbers = false)
    {
        var branches = new List<RegexSequenceSyntax>();
        var barTokens = new List<ScannedToken>();
        var supportsAlternation = Dialect.HasFeature(RegexDialectFeatures.Alternation);
        var alternationId = _nextAlternationId++;
        var firstCaptureNumber = AutoCaptureNumber;
        var nextCaptureNumber = AutoCaptureNumber;
        var completedBefore = _completedCaptures.Count;
        List<int>? completedInEarlierBranches = null;

        while (true)
        {
            _alternativePath.Add((alternationId, branches.Count));
            branches.Add(ParseSequence(insideGroup));
            _alternativePath.RemoveAt(_alternativePath.Count - 1);
            nextCaptureNumber = Math.Max(nextCaptureNumber, AutoCaptureNumber);

            var barPosition = PeekTriviaEnd();
            if (!supportsAlternation || IsAtBodyEnd(barPosition))
                break;

            var separatorLength = AlternationSeparatorLength(barPosition);
            if (separatorLength == 0)
                break;

            var trivia = TakeTrivia();
            var barStart = Scanner.Position;
            Scanner.Position += separatorLength;
            barTokens.Add(Scanner.Token(SyntaxKind.BarToken, barStart, trivia));

            if (resetsCaptureNumbers)
            {
                AutoCaptureNumber = firstCaptureNumber;
            }

            // A group closed in one branch has not matched in the next one, so a backreference there cannot see it.
            // The groups are set aside until the alternation ends, after which all of them have been closed.
            if (_completedCaptures.Count > completedBefore)
            {
                completedInEarlierBranches ??= [];
                completedInEarlierBranches.AddRange(_completedCaptures.Skip(completedBefore));
                _completedCaptures.RemoveRange(completedBefore, _completedCaptures.Count - completedBefore);
            }
        }

        if (completedInEarlierBranches is not null)
        {
            _completedCaptures.AddRange(completedInEarlierBranches);
        }

        if (resetsCaptureNumbers)
        {
            AutoCaptureNumber = nextCaptureNumber;
        }

        return new RegexAlternationSyntax(Interleave(branches, barTokens), Options);
    }

    /// <summary>
    /// Where the reading position is, as the branch it is in of every alternation around it, outermost first.
    /// </summary>
    /// <remarks>
    /// Two constructs can never both take part in a match when their paths first differ at the same alternation, which
    /// is the only case in which JavaScript lets two groups share a name.
    /// </remarks>
    protected (int Alternation, int Branch)[] CurrentAlternativePath => [.. _alternativePath];

    /// <summary>Whether two constructs, located by <see cref="CurrentAlternativePath"/>, can take part in the same match.</summary>
    protected static bool MightBothParticipate((int Alternation, int Branch)[] first, (int Alternation, int Branch)[] second)
    {
        for (var index = 0; index < first.Length && index < second.Length; index++)
        {
            if (first[index] == second[index])
                continue;

            return first[index].Alternation != second[index].Alternation;
        }

        return true;
    }

    /// <summary>Parses one branch: the terms that must match one after another.</summary>
    /// <remarks>
    /// The trivia in front of whatever comes next is peeked rather than claimed, because it may belong to the caller's
    /// <c>)</c> or <c>|</c> rather than to a term of this branch.
    /// </remarks>
    protected RegexSequenceSyntax ParseSequence(bool insideGroup)
    {
        var terms = new List<RegexTermSyntax>();
        var supportsAlternation = Dialect.HasFeature(RegexDialectFeatures.Alternation);

        while (true)
        {
            var triviaEnd = PeekTriviaEnd();
            if (IsAtBodyEnd(triviaEnd))
                break;

            if (supportsAlternation && AlternationSeparatorLength(triviaEnd) > 0)
                break;

            if (insideGroup && GroupCloseLength(triviaEnd) > 0)
                break;

            var before = Scanner.Position;
            IsAtSequenceStart = terms.Count == 0;
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

        return new RegexSequenceSyntax(SyntaxFactory.ListNode([.. terms]), Options);
    }

    /// <summary>
    /// Whether <paramref name="term"/> is something a quantifier can repeat. An assertion matches no characters, so
    /// repeating it is meaningless and most engines reject it.
    /// </summary>
    protected virtual bool IsQuantifiable(RegexTermSyntax term) => true;

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

            if (term is RegexQuantifiedSyntax && !AllowsStackedQuantifier(quantifier))
            {
                Scanner.AddDiagnostic(
                    JustParsedSpan(quantifier),
                    RegexDiagnosticIds.NestedQuantifiersNotParenthesized,
                    $"Nested quantifier '{quantifier.ToString().Trim()}' is not enclosed in parentheses.");
            }
            else if (!IsQuantifiable(term))
            {
                Scanner.AddDiagnostic(
                    JustParsedSpan(quantifier),
                    RegexDiagnosticIds.QuantifierAfterNothing,
                    $"Quantifier '{quantifier.ToString().Trim()}' has nothing to repeat.");
            }

            term = new RegexQuantifiedSyntax(term, quantifier, Options);
        }
    }

    /// <summary>Whether <paramref name="quantifier"/> may apply to a term that is already quantified.</summary>
    protected virtual bool AllowsStackedQuantifier(RegexQuantifierSyntax quantifier) => false;

    /// <summary>Returns whether a quantifier starts at <paramref name="position"/>.</summary>
    protected virtual bool IsQuantifierAt(int position)
    {
        if (position >= Text.Length)
            return false;

        if (BoundOpenLength(position) > 0)
            return IsWellFormedBoundAt(position);

        var ch = Text[position];
        if (ch is '+' or '?')
            return Dialect.HasFeature(RegexDialectFeatures.PlusAndQuestionQuantifiers);

        return ch == '*';
    }

    /// <summary>
    /// Whether a well-formed bound starts at <paramref name="position"/>, so that a brace that does not open one can
    /// stay an ordinary character. Deciding before anything is claimed is what lets the parser avoid a rewind.
    /// </summary>
    protected virtual bool IsWellFormedBoundAt(int position)
    {
        var openLength = BoundOpenLength(position);
        if (openLength == 0)
            return false;

        var index = SkipBoundSpaces(position + openLength);
        var digits = 0;
        while (index < Text.Length && char.IsAsciiDigit(Text[index]))
        {
            index++;
            digits++;
        }

        index = SkipBoundSpaces(index);
        var maxDigits = 0;
        if (index < Text.Length && Text[index] == ',')
        {
            index = SkipBoundSpaces(index + 1);
            while (index < Text.Length && char.IsAsciiDigit(Text[index]))
            {
                index++;
                maxDigits++;
            }

            index = SkipBoundSpaces(index);
            if (digits == 0 && (maxDigits == 0 || !AllowsOmittedMinimumBound))
                return false;
        }
        else if (digits == 0)
        {
            return false;
        }

        return BoundCloseLength(index) > 0;
    }

    /// <summary>The largest number a bound may be, or <see langword="null"/> when the dialect sets no limit.</summary>
    protected virtual long? MaxBoundValue => int.MaxValue;

    /// <summary>Whether spaces may surround the numbers of a bound, as in PCRE's <c>{ 2 , 3 }</c>.</summary>
    protected virtual bool AllowsSpacesInBounds => false;

    /// <summary>Whether a bound may leave out its minimum, as <c>{,3}</c> does in PCRE and GNU POSIX.</summary>
    protected virtual bool AllowsOmittedMinimumBound => false;

    private int SkipBoundSpaces(int index)
    {
        if (AllowsSpacesInBounds)
        {
            while (index < Text.Length && Text[index] is ' ' or '\t')
            {
                index++;
            }
        }

        return index;
    }

    /// <summary>Claims the spaces a bound may contain, as trivia of the token that follows them.</summary>
    private GreenNode? TakeBoundSpaces()
    {
        var start = Scanner.Position;
        Scanner.Position = SkipBoundSpaces(start);

        return Scanner.Position > start ? SyntaxFactory.List([SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, Text[start..Scanner.Position])]) : null;
    }

    private RegexQuantifierSyntax? ParseQuantifier(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;

        if (BoundOpenLength(start) == 0 && SimpleQuantifierLength(start, out var operatorCharacter) is var length && length > 0)
        {
            Scanner.Position += length;
            var operatorToken = Scanner.Token(QuantifierTokenKind(operatorCharacter), start, leadingTrivia);

            return new RegexSimpleQuantifierSyntax(operatorToken, ReadQuantifierModifier(), Options);
        }

        return ParseRangeQuantifier(leadingTrivia);
    }

    private static SyntaxKind QuantifierTokenKind(char ch) => ch switch
    {
        '*' => SyntaxKind.AsteriskToken,
        '+' => SyntaxKind.PlusToken,
        _ => SyntaxKind.QuestionToken,
    };

    /// <summary>Reads a <c>{n}</c>, <c>{n,}</c>, or <c>{n,m}</c> bound.</summary>
    /// <remarks>
    /// The caller only gets here once the look-ahead has confirmed the bound is well formed, so every token below is
    /// present. A bound that does not fit in an <see cref="int"/> is still consumed in full, so the span stays exact,
    /// and the clamped value is carried on the token so a consumer does not have to reparse the digits.
    /// </remarks>
    private RegexRangeQuantifierSyntax ParseRangeQuantifier(GreenNode? leadingTrivia)
    {
        var braceStart = Scanner.Position;
        Scanner.Position += BoundOpenLength(braceStart);
        var openBraceToken = Scanner.Token(SyntaxKind.OpenBraceToken, braceStart, leadingTrivia);

        var minToken = ReadBound(TakeBoundSpaces());
        ScannedToken commaToken = default;
        ScannedToken maxToken = default;
        var commaTrivia = TakeBoundSpaces();
        if (Scanner.Current == ',')
        {
            var commaStart = Scanner.Position;
            Scanner.Position++;
            commaToken = Scanner.Token(SyntaxKind.CommaToken, commaStart, commaTrivia);
            commaTrivia = TakeBoundSpaces();
            if (BoundCloseLength(Scanner.Position) == 0)
            {
                maxToken = ReadBound(commaTrivia);
                commaTrivia = TakeBoundSpaces();
            }
        }

        var closeStart = Scanner.Position;
        var closeLength = BoundCloseLength(closeStart);
        ScannedToken closeBraceToken;
        if (closeLength > 0)
        {
            Scanner.Position += closeLength;
            closeBraceToken = Scanner.Token(SyntaxKind.CloseBraceToken, closeStart, commaTrivia);
        }
        else
        {
            closeBraceToken = Scanner.MissingToken(SyntaxKind.CloseBraceToken);
            AddDiagnostic(TextSpan.FromBounds(braceStart, Scanner.Position), RegexDiagnosticIds.MalformedInterval, "Malformed interval: expected a bound such as '{2}', '{2,}', or '{2,5}'.");
        }

        if (closeBraceToken.IsPresent && !commaToken.IsPresent && minToken.Text.Trim().Length == 0)
        {
            AddDiagnostic(TextSpan.FromBounds(braceStart, closeBraceToken.End), RegexDiagnosticIds.MalformedInterval, "An interval must contain at least one bound.");
        }

        var quantifier = new RegexRangeQuantifierSyntax(openBraceToken, minToken, commaToken, maxToken, closeBraceToken, ReadQuantifierModifier(), Options);

        // The bounds are compared as written rather than as the clamped values, so a reversed pair of numbers too
        // large for an int is still reported.
        if (maxToken.IsPresent && maxToken.Text.Trim().Length > 0 && CompareDecimals(minToken.Text.Trim(), maxToken.Text.Trim()) > 0)
        {
            Scanner.AddDiagnostic(
                TextSpan.FromBounds(openBraceToken.Span.Start, closeBraceToken.End),
                RegexDiagnosticIds.ReversedQuantifierRange,
                FormattableString.Invariant($"Quantifier range {minToken.Text.Trim()},{maxToken.Text.Trim()} is reversed."));
        }

        return quantifier;
    }

    /// <summary>Compares two runs of decimal digits by the numbers they spell, whatever their size.</summary>
    private static int CompareDecimals(string left, string right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');

        return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
    }

    private ScannedToken ReadBound(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        long value = 0;
        while (char.IsAsciiDigit(Scanner.Current))
        {
            if (value <= int.MaxValue)
            {
                value = (value * 10) + (Scanner.Current - '0');
            }

            Scanner.Position++;
        }

        var max = MaxBoundValue;
        if (max is not null && value > max)
        {
            Scanner.AddDiagnostic(
                TextSpan.FromBounds(start, Scanner.Position),
                RegexDiagnosticIds.QuantifierOrCaptureGroupOutOfRange,
                max == int.MaxValue
                    ? "The quantifier or capture group number is larger than Int32.MaxValue."
                    : FormattableString.Invariant($"The quantifier bound is larger than {max}, the largest this dialect accepts."));
        }

        value = Math.Min(value, int.MaxValue);

        return Scanner.Token(SyntaxKind.NumberToken, start, leadingTrivia, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Reads the <c>?</c> or <c>+</c> that makes a quantifier lazy or possessive.</summary>
    /// <remarks>
    /// The engine scans trivia between the operator and the <c>?</c>, so <c>a{2,3} ?</c> is lazy in extended mode. The
    /// trivia becomes the modifier's leading trivia, which is where it round-trips from.
    /// </remarks>
    private ScannedToken ReadQuantifierModifier()
    {
        var triviaEnd = PeekTriviaEnd();
        var next = Scanner.CharAt(triviaEnd);

        var kind = next switch
        {
            '?' when Dialect.HasFeature(RegexDialectFeatures.LazyQuantifiers) => SyntaxKind.QuestionToken,
            '+' when Dialect.HasFeature(RegexDialectFeatures.PossessiveQuantifiers) => SyntaxKind.PlusToken,
            _ => SyntaxKind.None,
        };

        if (kind == SyntaxKind.None)
            return default;

        var trivia = TakeTrivia();
        var start = Scanner.Position;
        Scanner.Position++;

        return Scanner.Token(kind, start, trivia);
    }

    // ---- helpers shared by the dialect parsers ----

    /// <summary>
    /// Whether the pattern is read with ECMAScript behaviour: <c>[^]</c> is an empty negated class, octal escapes stop
    /// early, and a numeric backreference takes the longest prefix that names an existing group.
    /// </summary>
    /// <remarks>
    /// It is either asked for through .NET's own ECMAScript option or implied by the dialect, because for JavaScript
    /// those are simply the rules rather than an option.
    /// </remarks>
    protected bool UsesEcmaScriptBehavior =>
        (Options & RegexPatternOptions.EcmaScript) != RegexPatternOptions.None ||
        Dialect.Family == RegexDialectFamily.JavaScript;

    /// <summary>
    /// Whether an empty character class is allowed. In ECMAScript <c>[]</c> matches nothing and <c>[^]</c> matches
    /// anything; in .NET the same text is an unterminated class, so this follows the dialect rather than the options.
    /// </summary>
    protected bool AllowsEmptyCharacterClass => Dialect.Family == RegexDialectFamily.JavaScript;

    /// <summary>
    /// Whether the class set grammar is in effect: nested classes, <c>&amp;&amp;</c> and <c>--</c> operators, and
    /// <c>\q{…}</c> string disjunctions. That is what the JavaScript <c>v</c> flag turns on.
    /// </summary>
    protected bool UsesUnicodeSetsMode =>
        (Options & RegexPatternOptions.UnicodeSets) != RegexPatternOptions.None &&
        Dialect.HasFeature(RegexDialectFeatures.ClassSetOperations);

    /// <summary>Whether the pattern is read as a sequence of code points rather than of UTF-16 code units.</summary>
    /// <remarks>The <c>v</c> flag is the <c>u</c> flag with more, so either one turns this on.</remarks>
    protected bool UsesUnicodeMode => (Options & (RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets)) != RegexPatternOptions.None;

    protected int PeekTriviaEnd() => Scanner.PeekTriviaEnd(Options, Dialect);

    protected GreenNode? TakeTrivia() => Scanner.TakeTrivia(Options, Dialect);

    protected void AddDiagnostic(TextSpan span, string id, string message) => Scanner.AddDiagnostic(span, id, message);

    protected void AddDiagnostic(int start, string id, string message) =>
        Scanner.AddDiagnostic(TextSpan.FromBounds(start, Math.Max(start, Scanner.Position)), id, message);

    /// <summary>The number the next group that numbers itself will take.</summary>
    protected int AutoCaptureNumber { get; set; } = 1;

    /// <summary>Takes the next capture number, noting the slot when this is the numbering pass.</summary>
    protected int NoteAutoCapture(int position)
    {
        var number = AutoCaptureNumber++;
        CaptureBuilder?.NoteSlot(number, position);

        return number;
    }

    /// <summary>Notes a named group that takes the next capture number where it stands.</summary>
    protected int NoteNumberedCaptureName(string name, int position)
    {
        var number = AutoCaptureNumber++;
        CaptureBuilder?.NoteNumberedName(name, number, position);

        return number;
    }

    /// <summary>Whether this is the pass that only collects the capture groups, whose diagnostics are discarded.</summary>
    protected bool IsNumberingPass => CaptureBuilder is not null;

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
            _completedCaptures.Add(number);
        }
    }

    /// <summary>
    /// Whether the group <paramref name="number"/> has been closed before the reading position, in this branch or before
    /// the alternation around it, which is what a POSIX backreference needs.
    /// </summary>
    protected bool IsCaptureCompleted(int number) => _completedCaptures.Contains(number);

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
    protected RegexSkippedTextSyntax ConsumeRestAsText(int start, GreenNode? leadingTrivia = null)
    {
        Scanner.Position = Text.Length;

        return new RegexSkippedTextSyntax(Scanner.Token(SyntaxKind.BadToken, start, leadingTrivia), Options);
    }

    /// <summary>Builds a one-character skipped-text atom for input the grammar has no place for.</summary>
    protected RegexSkippedTextSyntax SkipOneCharacter(GreenNode? leadingTrivia, string id, string message)
    {
        var start = Scanner.Position;
        Scanner.Position++;
        var token = Scanner.Token(SyntaxKind.BadToken, start, leadingTrivia);
        AddDiagnostic(TextSpan.FromBounds(start, Scanner.Position), id, message);

        return new RegexSkippedTextSyntax(token, Options);
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
