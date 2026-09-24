using Green = Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A parsed TOML document.</summary>
/// <remarks>
/// Parsing never throws and never gives up: whatever the text says, the tree reproduces it exactly, and anything wrong
/// with it is reported through <see cref="GetDiagnostics"/>.
/// </remarks>
public sealed class TomlSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly TomlDocumentSyntax _root;
    private IReadOnlyList<Diagnostic>? _diagnostics;

    private TomlSyntaxTree(SourceText text, Green.TomlDocumentSyntax green, string? path)
    {
        _text = text;
        FilePath = path;
        _root = (TomlDocumentSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the root of the tree.</summary>
    public new TomlDocumentSyntax GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The TOML document to read.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseText(string text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), path);
    }

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree ParseText(SourceText text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new TomlSyntaxTree(text, new Green.LanguageParser(text).ParseDocument(), path);
    }

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static TomlSyntaxTree Create(TomlDocumentSyntax root, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new TomlSyntaxTree(SourceText.From(root.ToFullString()), (Green.TomlDocumentSyntax)root.Green, path);
    }

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public override IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics ??= base.GetDiagnostics();

    /// <summary>Returns a tree over <paramref name="newText"/>.</summary>
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
    public TomlSyntaxTree WithRoot(TomlDocumentSyntax root) => Create(root, FilePath);

    /// <summary>Describes how <paramref name="oldTree"/> would have to change to become this one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<TextChange> GetChanges(TomlSyntaxTree oldTree) => base.GetChanges(oldTree);

    /// <summary>Determines whether the two trees have the same structure and text.</summary>
    public bool IsEquivalentTo(TomlSyntaxTree? other) => base.IsEquivalentTo(other);

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root) => Create((TomlDocumentSyntax)root, FilePath);
}
