using Green = Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A parsed INI document.</summary>
/// <remarks>
/// Parsing never throws and never gives up: whatever the text says, the tree reproduces it exactly, and anything wrong
/// with it is reported through <see cref="GetDiagnostics"/>.
/// </remarks>
public sealed class IniSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly IniDocumentSyntax _root;
    private IReadOnlyList<Diagnostic>? _diagnostics;

    private IniSyntaxTree(SourceText text, Green.IniDocumentSyntax green, string? path)
    {
        _text = text;
        FilePath = path;
        _root = (IniDocumentSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the root of the tree.</summary>
    public new IniDocumentSyntax GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The INI document to read.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseText(string text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), path);
    }

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree ParseText(SourceText text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new IniSyntaxTree(text, new Green.LanguageParser(text).ParseDocument(), path);
    }

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static IniSyntaxTree Create(IniDocumentSyntax root, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new IniSyntaxTree(SourceText.From(root.ToFullString()), (Green.IniDocumentSyntax)root.Green, path);
    }

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public override IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics ??= base.GetDiagnostics();

    /// <summary>Returns a tree over <paramref name="newText"/>.</summary>
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

    /// <summary>Returns a tree whose root is <paramref name="root"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public IniSyntaxTree WithRoot(IniDocumentSyntax root) => Create(root, FilePath);

    /// <summary>Describes how <paramref name="oldTree"/> would have to change to become this one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<TextChange> GetChanges(IniSyntaxTree oldTree) => base.GetChanges(oldTree);

    /// <summary>Determines whether the two trees have the same structure and text.</summary>
    public bool IsEquivalentTo(IniSyntaxTree? other) => base.IsEquivalentTo(other);

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root) => Create((IniDocumentSyntax)root, FilePath);
}
