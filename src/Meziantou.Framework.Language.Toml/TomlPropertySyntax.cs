using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A key/value pair such as <c>name=value</c> or <c>name: value</c>.</summary>
public sealed class TomlPropertySyntax : TomlEntrySyntax
{
    internal TomlPropertySyntax(GreenNode green, SyntaxNode? parent, int position)
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
    public TomlPropertySyntax Update(SyntaxToken keyToken, SyntaxToken separatorToken, SyntaxToken valueToken)
    {
        if (keyToken.Node == Green.GetSlot(0) && separatorToken.Node == Green.GetSlot(1) && valueToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.TomlProperty(keyToken, separatorToken, valueToken).WithAnnotationsFrom(this);
    }

    public TomlPropertySyntax WithKeyToken(SyntaxToken keyToken) => Update(keyToken, SeparatorToken, ValueToken);

    /// <summary>Returns this property with a new key.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TomlPropertySyntax WithKey(string key) => WithKeyToken(SyntaxFactory.Key(key).WithTriviaFrom(KeyToken));

    public TomlPropertySyntax WithSeparatorToken(SyntaxToken separatorToken) => Update(KeyToken, separatorToken, ValueToken);
    public TomlPropertySyntax WithValueToken(SyntaxToken valueToken) => Update(KeyToken, SeparatorToken, valueToken);

    /// <summary>Returns this property with a new raw value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public TomlPropertySyntax WithValue(string value) => WithValueToken(SyntaxFactory.Value(value).WithTriviaFrom(ValueToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlProperty(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlProperty(this);
    }
}
