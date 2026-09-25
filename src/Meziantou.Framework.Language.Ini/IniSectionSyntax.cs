using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A section header such as <c>[database]</c>.</summary>
public sealed class IniSectionSyntax : IniEntrySyntax
{
    internal IniSectionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBracketToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken NameToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Gets the section name, without the whitespace around it.</summary>
    public string Name => NameToken.ValueText;

    public SyntaxToken CloseBracketToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the properties under this header: the ones that follow it, up to the next header.</summary>
    /// <remarks>A header that is not part of a document has none.</remarks>
    public IReadOnlyList<IniPropertySyntax> Properties => Parent is IniDocumentSyntax document ? document.GetSectionProperties(this) : [];

    /// <summary>Returns this section with the given parts, or itself when nothing changed.</summary>
    public IniSectionSyntax Update(SyntaxToken openBracketToken, SyntaxToken nameToken, SyntaxToken closeBracketToken)
    {
        if (openBracketToken.Node == Green.GetSlot(0) && nameToken.Node == Green.GetSlot(1) && closeBracketToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.IniSection(openBracketToken, nameToken, closeBracketToken).WithAnnotationsFrom(this);
    }

    public IniSectionSyntax WithOpenBracketToken(SyntaxToken openBracketToken) => Update(openBracketToken, NameToken, CloseBracketToken);
    public IniSectionSyntax WithNameToken(SyntaxToken nameToken) => Update(OpenBracketToken, nameToken, CloseBracketToken);

    /// <summary>Returns this section with a new name.</summary>
    /// <remarks>
    /// In a document, the name is checked the way the document reads it (see <see cref="IniDocumentSyntax.Options"/>), as
    /// <see cref="SyntaxFactory.SectionName(string, IniParseOptions)"/> checks it. Otherwise it is checked as
    /// <see cref="SyntaxFactory.SectionName(string)"/> checks it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> cannot be written as a section name.</exception>
    public IniSectionSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var options = (Parent as IniDocumentSyntax)?.Options ?? SyntaxFactory.StrictOptions;
        var token = SyntaxFactory.TrySectionName(name, options) ?? throw new ArgumentException($"'{name}' cannot be written as an INI section name.", nameof(name));
        return WithNameToken(token.WithTriviaFrom(NameToken));
    }

    public IniSectionSyntax WithCloseBracketToken(SyntaxToken closeBracketToken) => Update(OpenBracketToken, NameToken, closeBracketToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(IniSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitIniSection(this);
    }

    public override TResult? Accept<TResult>(IniSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitIniSection(this);
    }
}
