using Green = Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A parsed INI document.</summary>
/// <remarks>
/// Parsing never throws and never gives up: whatever the text says, the tree reproduces it exactly, and anything wrong
/// with it is reported through <see cref="GetDiagnostics"/>.
/// </remarks>
/// <example>
/// <code>
/// var tree = IniSyntaxTree.ParseText("[server]\nport = 8080\n");
/// var port = tree.GetRoot().GetValue("server", "port");
/// </code>
/// </example>
public sealed class IniSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly IniDocumentSyntax _root;
    private IReadOnlyList<Diagnostic>? _diagnostics;

    private IniSyntaxTree(SourceText text, Green.IniDocumentSyntax green, string? path)
    {
        _text = text;
        Options = green.Options;
        FilePath = path;
        _root = (IniDocumentSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    /// <summary>Gets the options the text was parsed with.</summary>
    public IniParseOptions Options { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the root of the tree.</summary>
    public new IniDocumentSyntax GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/> with <see cref="IniParseOptions.Default"/>.</summary>
    /// <param name="text">The INI document to read.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseText(string text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The INI document to read.</param>
    /// <param name="options">How to read it, or <see langword="null"/> for <see cref="IniParseOptions.Default"/>.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseText(string text, IniParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), options, path);
    }

    /// <summary>Parses <paramref name="text"/> with <see cref="IniParseOptions.Default"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseText(SourceText text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseText(SourceText text, IniParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        options ??= IniParseOptions.Default;
        return new IniSyntaxTree(text, new Green.LanguageParser(text, options).ParseDocument(), path);
    }

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text and its options from the root itself.</summary>
    /// <remarks>The diagnostics are those <paramref name="root"/> carries; the text is not parsed again.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree Create(IniDocumentSyntax root, string? path = null) => Create(root, options: null, path);

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <remarks>
    /// When <paramref name="options"/> are those of <paramref name="root"/>, the diagnostics are those it carries and the
    /// text is not parsed again. Other options read the text differently, so the text of <paramref name="root"/> is parsed
    /// again with them, and the tree has the nodes and diagnostics they give; the annotations of <paramref name="root"/>
    /// are not carried over then.
    /// </remarks>
    /// <param name="root">The root of the tree.</param>
    /// <param name="options">The options, or <see langword="null"/> for the <see cref="IniDocumentSyntax.Options"/> of <paramref name="root"/>.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree Create(IniDocumentSyntax root, IniParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var green = (Green.IniDocumentSyntax)root.Green;
        var text = SourceText.From(root.ToFullString());
        if (options is null || ReferenceEquals(options, green.Options))
            return new IniSyntaxTree(text, green, path);

        if (options.Equals(green.Options))
            return new IniSyntaxTree(text, green.WithOptions(options), path);

        return ParseText(text, options, path);
    }

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public override IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics ??= base.GetDiagnostics();

    /// <summary>Returns a tree over <paramref name="newText"/>, parsed with the same options.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
    public new IniSyntaxTree WithChangedText(SourceText newText) => (IniSyntaxTree)base.WithChangedText(newText);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public IniSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public IniSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return WithChangedText(_text.WithChanges(changes));
    }

    /// <summary>Returns a tree whose root is <paramref name="root"/>, read with the options of <paramref name="root"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public IniSyntaxTree WithRoot(IniDocumentSyntax root) => Create(root, options: null, FilePath);

    /// <summary>Describes how <paramref name="oldTree"/> would have to change to become this one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<TextChange> GetChanges(IniSyntaxTree oldTree) => base.GetChanges(oldTree);

    /// <summary>Determines whether the two trees have the same structure and text, and are read with equal options.</summary>
    /// <remarks>Trees read with different options can have the same text and structure, but not the same values.</remarks>
    public bool IsEquivalentTo([NotNullWhen(true)] IniSyntaxTree? other) => IsEquivalentTo((SyntaxTree?)other);

    /// <inheritdoc cref="IsEquivalentTo(IniSyntaxTree?)"/>
    public override bool IsEquivalentTo([NotNullWhen(true)] SyntaxTree? other)
        => other is IniSyntaxTree tree && Options.Equals(tree.Options) && base.IsEquivalentTo(other);

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, Options, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root) => Create((IniDocumentSyntax)root, options: null, FilePath);
}
