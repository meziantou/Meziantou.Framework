using Green = Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A parsed TOML document.</summary>
/// <remarks>
/// <para>
/// Parsing never throws and never gives up: whatever the text says, the tree reproduces it exactly, and anything wrong
/// with it is reported through <see cref="GetDiagnostics()"/> -- from a missing bracket to a key defined twice.
/// </para>
/// <para>
/// What breaks the grammar is carried by the nodes, and is what <see cref="SyntaxNode.ContainsDiagnostics"/> tells.
/// Whether a key or a table is defined twice, or a value extended after the fact, depends on the whole document
/// instead, so the tree works it out from its root the first time it is asked. A node carries none of these, and
/// neither does a node that is not part of a tree.
/// </para>
/// <para>
/// A tree made from a root with <see cref="Create(TomlDocumentSyntax, TomlParseOptions?, string?)"/> or
/// <see cref="WithRoot"/> reports the diagnostics of its text instead, which it parses again the first time it is
/// asked: an edit can build nodes that do not read back as themselves, such as a comment that hides the bracket after
/// it, and the options of the tree can be those of another version than the nodes were parsed with.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var tree = TomlSyntaxTree.ParseText("[server]\nport = 8080\n");
/// var port = (TomlIntegerSyntax)tree.GetRoot().Tables.Single().Properties.Single().Value;
/// </code>
/// </example>
public sealed class TomlSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly TomlDocumentSyntax _root;

    /// <summary>Whether <see cref="_root"/> is what parsing <see cref="_text"/> with <see cref="Options"/> gave, so that the nodes carry the diagnostics of the text.</summary>
    private readonly bool _isParsed;

    private Diagnostic[]? _diagnostics;
    private Diagnostic[]? _documentDiagnostics;

    private TomlSyntaxTree(SourceText text, TomlParseOptions options, Green.TomlDocumentSyntax green, string? path)
    {
        _text = text;
        Options = options;
        FilePath = path;
        _isParsed = true;
        _root = (TomlDocumentSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    private TomlSyntaxTree(TomlDocumentSyntax root, TomlParseOptions options, string? path)
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

        _root = ReferenceEquals(root.SyntaxTree, this) ? root : (TomlDocumentSyntax)root.Green.CreateRed();
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    /// <summary>Gets the options the text was parsed with.</summary>
    public TomlParseOptions Options { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the root of the tree.</summary>
    public new TomlDocumentSyntax GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/> as the latest version of TOML.</summary>
    /// <param name="text">The TOML document to read.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseText(string text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The TOML document to read.</param>
    /// <param name="options">How to read it, or <see langword="null"/> for <see cref="TomlParseOptions.Default"/>.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseText(string text, TomlParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), options, path);
    }

    /// <summary>Parses <paramref name="text"/> as the latest version of TOML.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseText(SourceText text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseText(SourceText text, TomlParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        options ??= TomlParseOptions.Default;
        return new TomlSyntaxTree(text, options, new Green.LanguageParser(text, options).ParseDocument(), path);
    }

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <remarks>
    /// <paramref name="root"/> becomes the root of the tree when it is not part of another one yet. The nodes are kept,
    /// but the diagnostics are those of the text: it is parsed again the first time they are asked for, so that they
    /// are right even when an edit built nodes that do not read back as themselves.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree Create(TomlDocumentSyntax root, string? path = null) => Create(root, options: null, path);

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <remarks>
    /// <paramref name="root"/> becomes the root of the tree when it is not part of another one yet. The nodes are kept,
    /// but the diagnostics are those of the text: it is parsed again with <paramref name="options"/> the first time they
    /// are asked for, so that they are right even when an edit built nodes that do not read back as themselves, or when
    /// the nodes were parsed as another version of TOML.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree Create(TomlDocumentSyntax root, TomlParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new TomlSyntaxTree(root, options ?? TomlParseOptions.Default, path);
    }

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public override IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics ??= ComputeDiagnostics();

    /// <summary>Gets the diagnostics at or below <paramref name="node"/>, including the keys and tables it defines twice.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public override IEnumerable<Diagnostic> GetDiagnostics(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!_isParsed)
            return Within(GetDiagnostics(), node.FullSpan);

        return Merge(base.GetDiagnostics(node), Within(GetDocumentDiagnostics(), node.FullSpan));
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

        return [.. Merge(base.GetDiagnostics(), GetDocumentDiagnostics())];
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

    /// <summary>Gets the diagnostics that depend on the whole document rather than on the grammar, in source order.</summary>
    private Diagnostic[] GetDocumentDiagnostics()
    {
        return _documentDiagnostics ??= [.. Green.DocumentValidator.Validate(_root.Green)
            .Select(info => info.ToDiagnostic(nodePosition: 0, _text))
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    /// <summary>Merges two lists of diagnostics that are each in source order, keeping the first list's first at the same position.</summary>
    private static IEnumerable<Diagnostic> Merge(IEnumerable<Diagnostic> first, IEnumerable<Diagnostic> second)
        => first.Concat(second).OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start);

    /// <summary>Returns a tree over <paramref name="newText"/>, parsed with the same options.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
    public new TomlSyntaxTree WithChangedText(SourceText newText) => (TomlSyntaxTree)base.WithChangedText(newText);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public TomlSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public TomlSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return WithChangedText(_text.WithChanges(changes));
    }

    /// <summary>Returns a tree whose root is <paramref name="root"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public TomlSyntaxTree WithRoot(TomlDocumentSyntax root) => Create(root, Options, FilePath);

    /// <summary>Describes how <paramref name="oldTree"/> would have to change to become this one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<TextChange> GetChanges(TomlSyntaxTree oldTree) => base.GetChanges(oldTree);

    /// <summary>Determines whether the two trees have the same structure and text.</summary>
    public bool IsEquivalentTo(TomlSyntaxTree? other) => base.IsEquivalentTo(other);

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, Options, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root)
        => Create(root as TomlDocumentSyntax ?? throw new ArgumentException($"The root of a TOML tree is a {nameof(TomlDocumentSyntax)}, not a {root.GetType().Name}.", nameof(root)), Options, FilePath);
}
