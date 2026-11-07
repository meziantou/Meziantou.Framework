namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// Root TOML syntax tree
/// </summary>
public sealed class DocumentSyntax : SyntaxNode
{
    /// <summary>
    /// Creates an instance of a <see cref="DocumentSyntax"/>
    /// </summary>
    public DocumentSyntax() : base(SyntaxKind.Document)
    {
        KeyValues = new SyntaxList<KeyValueSyntax>() {Parent = this};
        Tables = new SyntaxList<TableSyntaxBase>() { Parent = this };
        Diagnostics = new DiagnosticsBag();
    }

    /// <summary>
    /// Gets or sets a value indicating whether the document starts with a byte order mark (U+FEFF).
    /// </summary>
    /// <remarks>The byte order mark is written by <see cref="SyntaxNode.WriteTo"/>, before any trivia.</remarks>
    public bool HasByteOrderMark { get; set; }

    /// <summary>
    /// Gets the diagnostics attached to this document.
    /// </summary>
    public DiagnosticsBag Diagnostics { get; }

    /// <summary>
    /// Gets a boolean indicating if the <see cref="Diagnostics"/> has any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.HasErrors;

    /// <summary>
    /// Gets the list of <see cref="KeyValueSyntax"/>
    /// </summary>
    public SyntaxList<KeyValueSyntax> KeyValues { get; }

    /// <summary>
    /// Gets the list of tables (either <see cref="TableSyntax"/> or <see cref="TableArraySyntax"/>)
    /// </summary>
    public SyntaxList<TableSyntaxBase> Tables { get; }

    /// <inheritdoc />
    public override void Accept(SyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }

    /// <inheritdoc />
    public override int ChildrenCount => 2;

    /// <inheritdoc />
    protected override SyntaxNode GetChildImpl(int index)
    {
        return index == 0 ? (SyntaxNode)KeyValues : Tables;
    }
}
