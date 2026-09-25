using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A key/value pair such as <c>port = 8080</c>, in the document or in an inline table.</summary>
public sealed class TomlPropertySyntax : TomlEntrySyntax
{
    private TomlKeySyntax? _key;
    private TomlValueSyntax? _value;

    internal TomlPropertySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public TomlKeySyntax Key => GetRedAtZero(ref _key)!;

    public SyntaxToken EqualsToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    public TomlValueSyntax Value => GetRed(ref _value, 2)!;

    /// <summary>Returns this key/value pair with the given parts, or itself when nothing changed.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public TomlPropertySyntax Update(TomlKeySyntax key, SyntaxToken equalsToken, TomlValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (ReferenceEquals(key.Green, Green.GetSlot(0)) && equalsToken.Node == Green.GetSlot(1) && ReferenceEquals(value.Green, Green.GetSlot(2)))
            return this;

        return SyntaxFactory.TomlProperty(key, equalsToken, value).WithAnnotationsFrom(this);
    }

    public TomlPropertySyntax WithKey(TomlKeySyntax key) => Update(key, EqualsToken, Value);
    public TomlPropertySyntax WithEqualsToken(SyntaxToken equalsToken) => Update(Key, equalsToken, Value);
    public TomlPropertySyntax WithValue(TomlValueSyntax value) => Update(Key, EqualsToken, value);

    internal override SyntaxNode? GetNodeSlot(int index) => index switch
    {
        0 => GetRedAtZero(ref _key),
        2 => GetRed(ref _value, 2),
        _ => null,
    };

    internal override SyntaxNode? GetCachedSlot(int index) => index switch
    {
        0 => _key,
        2 => _value,
        _ => null,
    };

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
