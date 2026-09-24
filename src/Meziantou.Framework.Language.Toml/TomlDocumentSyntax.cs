using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A whole TOML document.</summary>
public sealed class TomlDocumentSyntax : TomlSyntaxNode
{
    private SyntaxNode? _entries;

    internal TomlDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the section, property, and skipped-text entries in source order.</summary>
    public SyntaxList<TomlEntrySyntax> Entries => new(GetRedAtZero(ref _entries));

    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Returns this document with the given parts, or itself when nothing changed.</summary>
    public TomlDocumentSyntax Update(SyntaxList<TomlEntrySyntax> entries, SyntaxToken endOfFileToken)
    {
        if (entries.Green == Green.GetSlot(0) && endOfFileToken.Node == Green.GetSlot(1))
            return this;

        return SyntaxFactory.TomlDocument(entries, endOfFileToken).WithAnnotationsFrom(this);
    }

    public TomlDocumentSyntax WithEntries(SyntaxList<TomlEntrySyntax> entries) => Update(entries, EndOfFileToken);
    public TomlDocumentSyntax WithEndOfFileToken(SyntaxToken endOfFileToken) => Update(Entries, endOfFileToken);
    public TomlDocumentSyntax AddEntries(params TomlEntrySyntax[] items) => WithEntries(Entries.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _entries) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _entries : null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlDocument(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlDocument(this);
    }
}
