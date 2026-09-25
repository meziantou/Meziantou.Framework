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

    /// <summary>Gets the key, without the whitespace around it.</summary>
    public string Key => KeyToken.ValueText;

    /// <summary>Gets <c>=</c> or <c>:</c>, or a missing token when the line holds only a key.</summary>
    public SyntaxToken SeparatorToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Gets the value token, whose text is the value as written, or a missing token when the line holds only a key.</summary>
    public SyntaxToken ValueToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the value.</summary>
    /// <remarks>
    /// The value is the text after the separator, without the whitespace around it or the comment after it. When the whole
    /// of it is in quotes, such as <c>"a;b"</c>, the quotes are left out; nothing is escaped inside them. A value that
    /// continues on the lines below it is those lines joined with <c>\n</c>, each without its indentation. A line holding
    /// only a key has the empty string.
    /// </remarks>
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
    /// <exception cref="ArgumentException"><paramref name="key"/> cannot be written as a key; see <see cref="SyntaxFactory.Key(string)"/>.</exception>
    public IniPropertySyntax WithKey(string key) => WithKeyToken(SyntaxFactory.Key(key).WithTriviaFrom(KeyToken));

    public IniPropertySyntax WithSeparatorToken(SyntaxToken separatorToken) => Update(KeyToken, separatorToken, ValueToken);
    public IniPropertySyntax WithValueToken(SyntaxToken valueToken) => Update(KeyToken, SeparatorToken, valueToken);

    /// <summary>Returns this property with a new value, quoted when it has to be, keeping the trivia around the old one.</summary>
    /// <remarks>A line that held only a key gets <c>=</c> between the key and the value.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> cannot be written as a value; see <see cref="SyntaxFactory.Value(string)"/>.</exception>
    public IniPropertySyntax WithValue(string value)
    {
        var valueToken = SyntaxFactory.Value(value).WithTriviaFrom(ValueToken);
        if (!SeparatorToken.IsMissing)
            return WithValueToken(valueToken);

        return Update(KeyToken, SyntaxFactory.Token(SyntaxKind.EqualsToken), valueToken);
    }

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
