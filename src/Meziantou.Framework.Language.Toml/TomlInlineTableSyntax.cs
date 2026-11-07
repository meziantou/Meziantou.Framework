using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>An inline table: braces around a comma-separated list of key/value pairs, such as <c>{ x = 1, y = 2 }</c>.</summary>
public sealed class TomlInlineTableSyntax : TomlValueSyntax
{
    private SyntaxNode? _properties;

    internal TomlInlineTableSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBraceToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the key/value pairs of the table. The commas between them are the separators of the list.</summary>
    public SeparatedSyntaxList<TomlPropertySyntax> Properties
    {
        get
        {
            var red = GetRed(ref _properties, 1);

            return red is null ? default : new SeparatedSyntaxList<TomlPropertySyntax>(new SyntaxNodeOrTokenList(red, GetChildIndex(1)));
        }
    }

    public SyntaxToken CloseBraceToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Returns this inline table with the given parts, or itself when nothing changed.</summary>
    public TomlInlineTableSyntax Update(SyntaxToken openBraceToken, SeparatedSyntaxList<TomlPropertySyntax> properties, SyntaxToken closeBraceToken)
    {
        if (openBraceToken.Node == Green.GetSlot(0) && properties.Green == Green.GetSlot(1) && closeBraceToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.TomlInlineTable(openBraceToken, properties, closeBraceToken).WithAnnotationsFrom(this);
    }

    public TomlInlineTableSyntax WithOpenBraceToken(SyntaxToken openBraceToken) => Update(openBraceToken, Properties, CloseBraceToken);
    public TomlInlineTableSyntax WithProperties(SeparatedSyntaxList<TomlPropertySyntax> properties) => Update(OpenBraceToken, properties, CloseBraceToken);
    public TomlInlineTableSyntax WithCloseBraceToken(SyntaxToken closeBraceToken) => Update(OpenBraceToken, Properties, closeBraceToken);

    /// <summary>Returns this inline table with <paramref name="items"/> added at the end, laid out the way the table already is.</summary>
    /// <remarks>
    /// In a table written on one line, each new key/value pair follows a comma and a space, and an empty table gets a
    /// space inside each brace. In one written a pair per line, each goes on a line of its own, indented as the last
    /// one is. A pair that ends with a comment gets a line break after it, or the comment would hide what follows it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or one of its items is <see langword="null"/>.</exception>
    public TomlInlineTableSyntax AddProperties(params TomlPropertySyntax[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var openBrace = OpenBraceToken;
        var closeBrace = CloseBraceToken;
        if (Properties.Count == 0 && items.Length > 0 && openBrace.TrailingTrivia.Count == 0 && closeBrace.LeadingTrivia.Count == 0)
        {
            openBrace = openBrace.WithTrailingTrivia(SyntaxFactory.Space);
            closeBrace = closeBrace.WithLeadingTrivia(SyntaxFactory.Space);
        }

        return Update(openBrace, SyntaxFactory.AddToList(Properties, items), closeBrace);
    }

    internal override SyntaxNode? GetNodeSlot(int index) => index == 1 ? GetRed(ref _properties, 1) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 1 ? _properties : null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlInlineTable(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlInlineTable(this);
    }
}
