namespace Meziantou.Framework.Yaml.Tokens;

/// <summary>Represents an alias token.</summary>
public class AnchorAlias : Token
{
    /// <summary>Gets the value of the alias.</summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchorAlias"/> class.
    /// </summary>
    /// <param name="value">The value of the anchor.</param>
    public AnchorAlias(string value)
        : this(value, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchorAlias"/> class.
    /// </summary>
    /// <param name="value">The value of the anchor.</param>
    /// <param name="start">The start position of the event.</param>
    /// <param name="end">The end position of the event.</param>
    public AnchorAlias(string value, Mark start, Mark end)
        : base(start, end)
    {
        Value = value;
    }
}