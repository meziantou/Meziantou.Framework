using Green = Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>A parsed JSON document.</summary>
/// <remarks>
/// Parsing never throws and never gives up: whatever the text says, the tree reproduces it exactly, and anything wrong
/// with it is reported through <see cref="GetDiagnostics"/>. Comments and trailing commas are accepted and kept.
/// </remarks>
/// <example>
/// <code>
/// var tree = JsonSyntaxTree.ParseText("""{ "name": "value" }""");
/// var name = ((JsonObjectSyntax)tree.GetRoot().Value!).GetMember("name");
/// </code>
/// </example>
public sealed class JsonSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly JsonDocumentSyntax _root;
    private IReadOnlyList<Diagnostic>? _diagnostics;

    private JsonSyntaxTree(SourceText text, Green.JsonDocumentSyntax green, string? path)
    {
        _text = text;
        FilePath = path;
        _root = (JsonDocumentSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the root of the tree.</summary>
    public new JsonDocumentSyntax GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The JSON to read.</param>
    /// <param name="path">Where the text came from, for the diagnostics to refer to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Json)] string text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), path);
    }

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxTree ParseText(SourceText text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new JsonSyntaxTree(text, new Green.LanguageParser(text).ParseDocument(), path);
    }

    /// <summary>Creates a tree over <paramref name="root"/>, taking its text from the root itself.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxTree Create(JsonDocumentSyntax root, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new JsonSyntaxTree(SourceText.From(root.ToFullString()), (Green.JsonDocumentSyntax)root.Green, path);
    }

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public new IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics ??= [.. base.GetDiagnostics()];

    /// <summary>Returns a tree over <paramref name="newText"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
    public new JsonSyntaxTree WithChangedText(SourceText newText) => (JsonSyntaxTree)base.WithChangedText(newText);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public JsonSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    /// <summary>Returns a tree over this text with <paramref name="changes"/> applied.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public JsonSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return WithChangedText(_text.WithChanges(changes));
    }

    /// <summary>Returns a tree whose root is <paramref name="root"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public JsonSyntaxTree WithRoot(JsonDocumentSyntax root) => Create(root, FilePath);

    /// <summary>Describes how <paramref name="oldTree"/> would have to change to become this one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<TextChange> GetChanges(JsonSyntaxTree oldTree) => base.GetChanges(oldTree);

    /// <summary>Determines whether the two trees have the same structure and text.</summary>
    public bool IsEquivalentTo(JsonSyntaxTree? other) => base.IsEquivalentTo(other);

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root) => Create((JsonDocumentSyntax)root, FilePath);
}
