namespace Meziantou.Framework.Yaml.Tokens;

/// <summary>Represents a scalar token.</summary>
public class Scalar : Token
{
    /// <summary>Gets the value.</summary>
    /// <value>The value.</value>
    public string Value { get; }

    /// <summary>Gets the style.</summary>
    /// <value>The style.</value>
    public ScalarStyle Style { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public Scalar(string value)
        : this(value, ScalarStyle.Any)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="style">The style.</param>
    public Scalar(string value, ScalarStyle style)
        : this(value, style, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="style">The style.</param>
    /// <param name="start">The start position of the token.</param>
    /// <param name="end">The end position of the token.</param>
    public Scalar(string value, ScalarStyle style, Mark start, Mark end)
        : base(start, end)
    {
        Value = value;
        Style = style;
    }
}