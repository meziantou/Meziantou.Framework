using Meziantou.Framework.Language.Regex.Internals;
using Green = Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Regex;

/// <summary>A parsed regular expression.</summary>
/// <remarks>
/// Parsing never throws. Whatever the pattern says, the tree reproduces it exactly, and anything wrong with it is
/// reported through <see cref="GetDiagnostics"/>.
/// </remarks>
public sealed class RegexSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly RegexPatternSyntax _root;
    private readonly IReadOnlyList<Diagnostic> _diagnostics;

    private RegexSyntaxTree(SourceText sourceText, RegexParseOptions options, Green.RegexPatternSyntax green, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<RegexCaptureInfo> captures, RegexPatternOptions patternOptions)
    {
        _text = sourceText;
        _diagnostics = diagnostics;
        Options = options;
        Captures = captures;
        PatternOptions = patternOptions;
        _root = (RegexPatternSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    public RegexParseOptions Options { get; }

    /// <summary>Gets the dialect the pattern was parsed as.</summary>
    public RegexDialect Dialect => Options.Dialect;

    /// <summary>Gets the options in effect at the start of the pattern, including any read from a literal's flags.</summary>
    public RegexPatternOptions PatternOptions { get; }

    /// <summary>Gets the capture groups the pattern declares, in the order the engine numbers them.</summary>
    public IReadOnlyList<RegexCaptureInfo> Captures { get; }

    public override string? FilePath => null;

    public override SourceText GetText() => _text;

    /// <summary>Gets the root of the tree.</summary>
    public new RegexPatternSyntax GetRoot() => _root;

    /// <summary>Gets everything the parser had to report, in source order.</summary>
    public override IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics;

    /// <summary>Parses <paramref name="pattern"/>. Never throws; problems are reported as diagnostics.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static RegexSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        return ParseText(pattern, new RegexParseOptions(dialect));
    }

    /// <inheritdoc cref="ParseText(string, RegexDialect)"/>
    public static RegexSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Parse(pattern ?? string.Empty, options, literal: null);
    }

    /// <summary>
    /// Parses a JavaScript regular-expression literal such as <c>/a+/giu</c>, reading the flag letters into
    /// <see cref="PatternOptions"/> and keeping the delimiters on the root so the literal round-trips.
    /// </summary>
    /// <remarks>
    /// The dialect is always <see cref="RegexDialect.JavaScript"/>: only that dialect has literals. Text that follows
    /// the flags is reported as <c>REGEX0204</c> and kept as skipped text.
    /// </remarks>
    public static RegexSyntaxTree ParseJavaScriptLiteral(string literal)
    {
        var text = literal ?? string.Empty;
        var parsed = JavaScriptLiteral.Split(text);

        return Parse(text, new RegexParseOptions(RegexDialect.JavaScript) { PatternOptions = parsed.Options }, parsed);
    }

    /// <summary>Returns a tree over this pattern with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public RegexSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    /// <inheritdoc cref="WithChanges(TextChange[])"/>
    public RegexSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return Reparse(_text.WithChanges(changes).Text);
    }

    /// <summary>Reparses <paramref name="text"/> the way this tree was parsed, literal delimiters included.</summary>
    /// <remarks>
    /// A tree built by <see cref="ParseJavaScriptLiteral"/> has to go back through it. Reparsing <c>/a/g</c> as a bare
    /// pattern would read the delimiters as literal slashes and the flags as ordinary characters, so an edit anywhere
    /// in the pattern would quietly destroy the structure around it.
    /// </remarks>
    internal RegexSyntaxTree Reparse(string text) => _root.IsJavaScriptLiteral ? ParseJavaScriptLiteral(text) : ParseText(text, Options);

    /// <summary>
    /// Returns the edit that turns <paramref name="oldTree"/>'s text into this tree's text. The common prefix and
    /// suffix are trimmed, so an edit in the middle of a pattern reports only the part that actually differs.
    /// </summary>
    /// <remarks>
    /// This compares the two texts rather than the two trees. Every edit to a pattern reparses it, so the trees never
    /// share nodes and a structural comparison would report the whole pattern as replaced.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public override IReadOnlyList<TextChange> GetChanges(SyntaxTree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);

        var newText = GetText();

        return [.. newText.GetChangeRanges(oldTree.GetText()).Select(range =>
            new TextChange(range.Span, newText.ToString(new TextSpan(range.Span.Start, range.NewLength))))];
    }

    /// <summary>
    /// Compares this tree with <paramref name="other"/> structurally, ignoring extended-mode whitespace and comments.
    /// Two patterns parsed as different dialects, or with different options, are never equivalent.
    /// </summary>
    /// <remarks>
    /// Identical text is a shortcut only when the options match as well. The same characters read with and without
    /// <see cref="RegexPatternOptions.IgnorePatternWhitespace"/> are different trees -- a space is a term in one and
    /// trivia in the other -- so comparing the text alone would call them equivalent.
    /// </remarks>
    public bool IsEquivalentTo(RegexSyntaxTree? other)
    {
        if (other is null || other.Dialect != Dialect || other.PatternOptions != PatternOptions)
            return false;

        return string.Equals(_text.Text, other._text.Text, StringComparison.Ordinal) || _root.IsEquivalentTo(other._root);
    }

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => Reparse(newText.Text);

    protected override SyntaxTree WithRootCore(SyntaxNode root) => Reparse(root.ToFullString());

    private static RegexSyntaxTree Parse(string text, RegexParseOptions options, JavaScriptLiteral? literal)
    {
        // Built once and shared by both passes and the tree, so a diagnostic's location points at the same source
        // text instance the tree exposes.
        var source = SourceText.From(text);

        // A pattern is numbered before it is parsed, because a backreference may name a group declared after it. The
        // numbering walk is the same parser over the same text, so the two cannot disagree about where the groups are.
        var captureTable = CreateParser(source, options, literal).CollectCaptureTable();

        var parser = CreateParser(source, options, literal);
        var root = parser.ParsePattern(captureTable);

        return new RegexSyntaxTree(source, options, root, [.. parser.Diagnostics], parser.Captures, options.PatternOptions);
    }

    /// <summary>The dialect family selects the parser; dialect features handle the differences within a family.</summary>
    private static Green.RegexParser CreateParser(SourceText source, RegexParseOptions options, JavaScriptLiteral? literal) => options.Dialect.Family switch
    {
        RegexDialectFamily.JavaScript => new Green.JavaScriptRegexParser(source, options, literal),
        RegexDialectFamily.Pcre => new Green.PcreRegexParser(source, options),
        RegexDialectFamily.Posix => new Green.PosixRegexParser(source, options),
        _ => new Green.NetRegexParser(source, options),
    };
}
