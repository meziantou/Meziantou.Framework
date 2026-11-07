using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A string, in any of its four forms: basic, literal, and the multi-line form of each.</summary>
public sealed class TomlStringSyntax : TomlValueSyntax
{
    internal TomlStringSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken StringToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the text of the string, with its quotes removed and its escape sequences resolved.</summary>
    /// <remarks>
    /// <para>Line breaks in a multi-line string are line feeds, whichever line break the text uses.</para>
    /// <para>
    /// When the string is in error, which <see cref="SyntaxNode.ContainsDiagnostics"/> tells, this is a best guess:
    /// an escape sequence that is not valid is left out or kept in part, and a string without its closing quotes runs
    /// to the end of its line, or of the text for a multi-line one. Check for diagnostics before relying on it.
    /// </para>
    /// </remarks>
    public string Value => StringToken.ValueText;

    /// <summary>Gets a value indicating whether the string is written between three quotes, and may span lines.</summary>
    public bool IsMultiLine => StringToken.Kind() is SyntaxKind.MultiLineBasicStringToken or SyntaxKind.MultiLineLiteralStringToken;

    /// <summary>Gets a value indicating whether the string is written between single quotes, and has no escape sequences.</summary>
    public bool IsLiteral => StringToken.Kind() is SyntaxKind.LiteralStringToken or SyntaxKind.MultiLineLiteralStringToken;

    /// <summary>Returns this value with a different token, or itself when nothing changed.</summary>
    public TomlStringSyntax Update(SyntaxToken stringToken)
    {
        if (stringToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.TomlString(stringToken).WithAnnotationsFrom(this);
    }

    public TomlStringSyntax WithStringToken(SyntaxToken stringToken) => Update(stringToken);

    /// <summary>Returns this string with a new value, written as a basic string and escaped as TOML requires.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public TomlStringSyntax WithValue(string value) => Update(SyntaxFactory.Literal(value).WithTriviaFrom(StringToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlString(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlString(this);
    }
}
