using System;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// A string TOML syntax value node.
/// </summary>
public sealed class StringValueSyntax : BareKeyOrStringValueSyntax
{
    private SyntaxToken? _token;

    /// <summary>
    /// Creates a new instance of <see cref="StringValueSyntax"/>
    /// </summary>
    public StringValueSyntax() : base(SyntaxKind.String)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="StringValueSyntax"/>
    /// </summary>
    /// <param name="text">String value used for this node</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains an unpaired surrogate, which TOML cannot represent.</exception>
    public StringValueSyntax(string text) : this()
    {
        ArgumentNullException.ThrowIfNull(text);
        var index = CharHelper.IndexOfUnpairedSurrogate(text);
        if (index >= 0)
        {
            throw new ArgumentException(CharHelper.GetUnpairedSurrogateMessage(text, index, "string"), nameof(text));
        }

        Token = new SyntaxToken(TokenKind.String, $"\"{text.EscapeForToml()}\"");
        Value = text;
    }

    /// <summary>
    /// The token of the string.
    /// </summary>
    public SyntaxToken? Token
    {
        get => _token;
        set => ParentToThis(ref _token, value, value != null && value.TokenKind.IsString(), "string");
    }

    /// <summary>
    /// The associated parsed string value
    /// </summary>
    public string? Value { get; set; }

    /// <inheritdoc />
    public override void Accept(SyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }

    /// <inheritdoc />
    public override int ChildrenCount => 1;

    /// <inheritdoc />
    protected override SyntaxNode? GetChildImpl(int index)
    {
        return Token;
    }

    /// <inheritdoc />
    protected override string ToDebuggerDisplay()
    {
        return $"{base.ToDebuggerDisplay()}: {(Value is not null ? TomlFormatHelper.ToString(Value, TomlPropertyDisplayKind.Default) : string.Empty)}";
    }
}
