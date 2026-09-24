using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A key/value pair such as <c>name=value</c> or <c>name: value</c>.</summary>
public sealed class IniPropertySyntax : IniEntrySyntax
{
    internal IniPropertySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken KeyToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the key.</summary>
    public string Key => KeyToken.ValueText;

    public SyntaxToken SeparatorToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxToken ValueToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the raw value text.</summary>
    public string Value => ValueToken.ValueText;

    /// <summary>Returns this property with the given parts, or itself when nothing changed.</summary>
    public IniPropertySyntax Update(SyntaxToken keyToken, SyntaxToken separatorToken, SyntaxToken valueToken)
    {
        if (keyToken.Node == Green.GetSlot(0) && separatorToken.Node == Green.GetSlot(1) && valueToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.IniProperty(keyToken, separatorToken, valueToken).WithAnnotationsFrom(this);
    }

    public IniPropertySyntax WithKeyToken(SyntaxToken keyToken) => Update(keyToken, SeparatorToken, ValueToken);

    /// <summary>Returns this property with a new key.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public IniPropertySyntax WithKey(string key) => WithKeyToken(SyntaxFactory.Key(key).WithTriviaFrom(KeyToken));

    public IniPropertySyntax WithSeparatorToken(SyntaxToken separatorToken) => Update(KeyToken, separatorToken, ValueToken);
    public IniPropertySyntax WithValueToken(SyntaxToken valueToken) => Update(KeyToken, SeparatorToken, valueToken);

    /// <summary>Returns this property with a new raw value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public IniPropertySyntax WithValue(string value) => WithValueToken(SyntaxFactory.Value(value).WithTriviaFrom(ValueToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(IniSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitIniProperty(this);
    }

    public override TResult? Accept<TResult>(IniSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitIniProperty(this);
    }
}
