using Green = Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>An XML document read from source text, together with what is wrong with it.</summary>
/// <example>
/// <code>
/// var tree = XmlSyntaxTree.ParseText(xml);
/// var updated = tree.WithChanges(new TextChange(new TextSpan(0, 0), "&lt;!--generated--&gt;"));
/// </code>
/// </example>
public sealed class XmlSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly XmlDocumentSyntax _root;
    private readonly IReadOnlyList<Diagnostic> _diagnostics;

    private XmlSyntaxTree(SourceText text, Green.XmlDocumentSyntax green, IReadOnlyList<Diagnostic> diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
        _root = (XmlDocumentSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    public override string? FilePath => null;

    public override SourceText GetText() => _text;

    /// <summary>Gets the document this tree holds.</summary>
    public new XmlDocumentSyntax GetRoot() => _root;

    /// <summary>Gets everything wrong with the document, in the order the parser found it.</summary>
    public new IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics;

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XmlSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Xml)] string text) => ParseText(SourceText.From(text));

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XmlSyntaxTree ParseText(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var parser = new Green.LanguageParser(text);
        var root = parser.ParseDocument();

        return new XmlSyntaxTree(text, root, parser.Diagnostics);
    }

    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public XmlSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public XmlSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return ParseText(_text.WithChanges(changes));
    }

    /// <summary>
    /// Determines whether the two trees say the same thing, ignoring how they are laid out.
    /// </summary>
    /// <remarks>
    /// This compares the documents rather than their trees, because two documents that differ only in whitespace
    /// between tags have genuinely different trees: that whitespace is character data, not trivia.
    /// </remarks>
    public bool IsEquivalentTo(XmlSyntaxTree? other)
    {
        if (other is null)
            return false;

        if (string.Equals(_text.Text, other._text.Text, StringComparison.Ordinal))
            return true;

        return string.Equals(Canonicalize(_text.Text), Canonicalize(other._text.Text), StringComparison.Ordinal);
    }

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText);

    protected override SyntaxTree WithRootCore(SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return ParseText(root.ToFullString());
    }

    private static string Canonicalize(string text)
    {
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(text, System.Xml.Linq.LoadOptions.PreserveWhitespace);

            return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        }
        catch (System.Xml.XmlException)
        {
            return CollapseWhitespace(text);
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var seenWhitespace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                seenWhitespace = true;
                continue;
            }

            if (seenWhitespace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(character);
            seenWhitespace = false;
        }

        return builder.ToString();
    }
}
