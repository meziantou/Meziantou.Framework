using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A whole INI document.</summary>
public sealed class IniDocumentSyntax : IniSyntaxNode
{
    private SyntaxNode? _entries;

    internal IniDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the section, property, and skipped-text entries in source order.</summary>
    public SyntaxList<IniEntrySyntax> Entries => new(GetRedAtZero(ref _entries));

    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Returns this document with the given parts, or itself when nothing changed.</summary>
    public IniDocumentSyntax Update(SyntaxList<IniEntrySyntax> entries, SyntaxToken endOfFileToken)
    {
        if (entries.Green == Green.GetSlot(0) && endOfFileToken.Node == Green.GetSlot(1))
            return this;

        return SyntaxFactory.IniDocument(entries, endOfFileToken).WithAnnotationsFrom(this);
    }

    public IniDocumentSyntax WithEntries(SyntaxList<IniEntrySyntax> entries) => Update(entries, EndOfFileToken);
    public IniDocumentSyntax WithEndOfFileToken(SyntaxToken endOfFileToken) => Update(Entries, endOfFileToken);
    public IniDocumentSyntax AddEntries(params IniEntrySyntax[] items) => WithEntries(Entries.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _entries) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _entries : null;

    public override void Accept(IniSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitIniDocument(this);
    }

    public override TResult? Accept<TResult>(IniSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitIniDocument(this);
    }
}
