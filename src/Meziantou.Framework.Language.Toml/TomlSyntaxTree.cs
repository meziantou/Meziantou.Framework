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
/// instead, so the tree works it out from its root the first time it is asked, and again for every new tree an edit
/// produces. A node carries none of these, and neither does a node that is not part of a tree.
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
    private IReadOnlyList<Diagnostic>? _diagnostics;
    private Diagnostic[]? _documentDiagnostics;

    private TomlSyntaxTree(SourceText text, TomlParseOptions options, Green.TomlDocumentSyntax green, string? path)
    {
        _text = text;
        Options = options;
        FilePath = path;
        _root = (TomlDocumentSyntax)green.CreateRed();
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
    /// The text is not parsed again: the grammar diagnostics are those <paramref name="root"/> carries. Keys and
    /// tables defined twice are checked over the new root.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree Create(TomlDocumentSyntax root, string? path = null) => Create(root, options: null, path);

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <remarks>
    /// The text is not parsed again: the grammar diagnostics are those <paramref name="root"/> carries. Keys and
    /// tables defined twice are checked over the new root.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree Create(TomlDocumentSyntax root, TomlParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new TomlSyntaxTree(SourceText.From(root.ToFullString()), options ?? TomlParseOptions.Default, (Green.TomlDocumentSyntax)root.Green, path);
    }

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public override IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics ??= [.. Merge(base.GetDiagnostics(), GetDocumentDiagnostics())];

    /// <summary>Gets the diagnostics at or below <paramref name="node"/>, including the keys and tables it defines twice.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public override IEnumerable<Diagnostic> GetDiagnostics(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var span = node.FullSpan;
        return Merge(base.GetDiagnostics(node), GetDocumentDiagnostics().Where(diagnostic => span.Contains(diagnostic.Location.SourceSpan)));
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

    protected override SyntaxTree WithRootCore(SyntaxNode root) => Create((TomlDocumentSyntax)root, Options, FilePath);
}
