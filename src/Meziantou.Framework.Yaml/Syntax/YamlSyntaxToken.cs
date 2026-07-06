namespace Meziantou.Framework.Yaml.Syntax;

/// <summary>Represents a YAML syntax token.</summary>
public readonly struct YamlSyntaxToken
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlSyntaxToken"/> struct.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="span">The token span.</param>
    /// <param name="text">The token text.</param>
    public YamlSyntaxToken(YamlSyntaxKind kind, YamlSourceSpan span, string text)
    {
        Kind = kind;
        Span = span;
        Text = text;
    }

    /// <summary>Gets the token kind.</summary>
    public YamlSyntaxKind Kind { get; }

    /// <summary>Gets the token span.</summary>
    public YamlSourceSpan Span { get; }

    /// <summary>Gets the token text.</summary>
    public string Text { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Kind} {Span}";
    }
}
