using Green = Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>A parsed CSS style sheet.</summary>
/// <remarks>
/// <para>
/// Parsing never throws and never gives up: whatever the text says, the tree reproduces it exactly, and anything wrong
/// with it is reported through <see cref="GetDiagnostics()"/> -- from an unclosed block to a selector a browser would
/// reject. An error is something a browser drops; a warning is something it accepts, or that may only be newer than
/// this parser.
/// </para>
/// <para>
/// A tree made from a root with <see cref="Create(CssStyleSheetSyntax, CssParseOptions?, string?)"/> or
/// <see cref="WithRoot"/> reports the diagnostics of its text instead, which it parses again the first time it is
/// asked: an edit can build nodes that do not read back as themselves, such as two type selectors put next to each
/// other without the whitespace that would make them a descendant combinator.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var tree = CssSyntaxTree.ParseText(".card { color: red; &amp;:hover { color: blue } }");
/// var rule = (CssQualifiedRuleSyntax)tree.GetRoot().Statements[0];
/// var selectors = (CssSelectorListSyntax)rule.Prelude!;
/// </code>
/// </example>
public sealed class CssSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly CssStyleSheetSyntax _root;

    /// <summary>Whether <see cref="_root"/> is what parsing <see cref="_text"/> with <see cref="Options"/> gave, so that the nodes carry the diagnostics of the text.</summary>
    private readonly bool _isParsed;

    private Diagnostic[]? _diagnostics;

    private CssSyntaxTree(SourceText text, CssParseOptions options, Green.CssStyleSheetSyntax green, string? path)
    {
        _text = text;
        Options = options;
        FilePath = path;
        _isParsed = true;
        _root = (CssStyleSheetSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    private CssSyntaxTree(CssStyleSheetSyntax root, CssParseOptions options, string? path)
    {
        _text = SourceText.From(root.ToFullString());
        Options = options;
        FilePath = path;

        // The root the caller holds becomes the root of the tree when it belongs to none yet, as in Roslyn, so that the
        // nodes the caller holds report the diagnostics of the tree. A root that already belongs to a tree is copied.
        if (root.Position == 0 && root.SyntaxTree is null)
        {
            root.AttachToTree(this);
        }

        _root = ReferenceEquals(root.SyntaxTree, this) ? root : (CssStyleSheetSyntax)root.Green.CreateRed();
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    /// <summary>Gets the options the text was parsed with.</summary>
    public CssParseOptions Options { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the root of the tree.</summary>
    public new CssStyleSheetSyntax GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/> as a style sheet.</summary>
    /// <param name="text">The CSS to read.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static CssSyntaxTree ParseText(string text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The CSS to read.</param>
    /// <param name="options">How to read it, or <see langword="null"/> for <see cref="CssParseOptions.Default"/>.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static CssSyntaxTree ParseText(string text, CssParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), options, path);
    }

    /// <summary>Parses <paramref name="text"/> as a style sheet.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static CssSyntaxTree ParseText(SourceText text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static CssSyntaxTree ParseText(SourceText text, CssParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        options ??= CssParseOptions.Default;
        return new CssSyntaxTree(text, options, new Green.LanguageParser(text, options).ParseStyleSheet(), path);
    }

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <remarks>
    /// <paramref name="root"/> becomes the root of the tree when it is not part of another one yet. The nodes are kept,
    /// but the diagnostics are those of the text: it is parsed again the first time they are asked for, so that they
    /// are right even when an edit built nodes that do not read back as themselves.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static CssSyntaxTree Create(CssStyleSheetSyntax root, string? path = null) => Create(root, options: null, path);

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <remarks>
    /// <paramref name="root"/> becomes the root of the tree when it is not part of another one yet. The nodes are kept,
    /// but the diagnostics are those of the text: it is parsed again with <paramref name="options"/> the first time they
    /// are asked for, so that they are right even when an edit built nodes that do not read back as themselves.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static CssSyntaxTree Create(CssStyleSheetSyntax root, CssParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new CssSyntaxTree(root, options ?? CssParseOptions.Default, path);
    }

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public override IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics ??= ComputeDiagnostics();

    /// <summary>Gets the diagnostics at or below <paramref name="node"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public override IEnumerable<Diagnostic> GetDiagnostics(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return _isParsed ? base.GetDiagnostics(node) : Within(GetDiagnostics(), node.FullSpan);
    }

    /// <summary>Gets the diagnostics on <paramref name="token"/> and the trivia around it.</summary>
    public override IEnumerable<Diagnostic> GetDiagnostics(SyntaxToken token)
        => _isParsed ? base.GetDiagnostics(token) : Within(GetDiagnostics(), token.FullSpan);

    /// <summary>Gets the diagnostics on <paramref name="trivia"/>.</summary>
    public override IEnumerable<Diagnostic> GetDiagnostics(SyntaxTrivia trivia)
        => _isParsed ? base.GetDiagnostics(trivia) : Within(GetDiagnostics(), trivia.FullSpan);

    private Diagnostic[] ComputeDiagnostics()
    {
        // The nodes of a tree made from a root may not be what their text reads as, so the text is what is checked.
        if (!_isParsed)
            return [.. ParseText(_text, Options, FilePath).GetDiagnostics()];

        return [.. base.GetDiagnostics()];
    }

    /// <summary>Gets the diagnostics of <paramref name="diagnostics"/>, which are in source order, that lie within <paramref name="span"/>.</summary>
    /// <remarks>A binary search finds the first one, so asking every node of a tree for its diagnostics stays linear.</remarks>
    private static List<Diagnostic> Within(IReadOnlyList<Diagnostic> diagnostics, TextSpan span)
    {
        var low = 0;
        var high = diagnostics.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (diagnostics[middle].Location.SourceSpan.Start < span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var result = new List<Diagnostic>();
        for (var i = low; i < diagnostics.Count && diagnostics[i].Location.SourceSpan.Start <= span.End; i++)
        {
            if (span.Contains(diagnostics[i].Location.SourceSpan))
            {
                result.Add(diagnostics[i]);
            }
        }

        return result;
    }

    /// <summary>Returns a tree over <paramref name="newText"/>, parsed with the same options.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
    public new CssSyntaxTree WithChangedText(SourceText newText) => (CssSyntaxTree)base.WithChangedText(newText);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public CssSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public CssSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return WithChangedText(_text.WithChanges(changes));
    }

    /// <summary>Returns a tree whose root is <paramref name="root"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public CssSyntaxTree WithRoot(CssStyleSheetSyntax root) => Create(root, Options, FilePath);

    /// <summary>Describes how <paramref name="oldTree"/> would have to change to become this one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<TextChange> GetChanges(CssSyntaxTree oldTree) => base.GetChanges(oldTree);

    /// <summary>Determines whether the two trees have the same structure and text.</summary>
    public bool IsEquivalentTo(CssSyntaxTree? other) => base.IsEquivalentTo(other);

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, Options, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root)
        => Create(root as CssStyleSheetSyntax ?? throw new ArgumentException($"The root of a CSS tree is a {nameof(CssStyleSheetSyntax)}, not a {root.GetType().Name}.", nameof(root)), Options, FilePath);
}
