namespace Meziantou.Framework.Yaml.Events;

/// <summary>Represents an alias event.</summary>
public class AnchorAlias : ParsingEvent
{
    /// <summary>
    /// Gets a value indicating the variation of depth caused by this event.
    /// The value can be either -1, 0 or 1. For start events, it will be 1,
    /// for end events, it will be -1, and for the remaining events, it will be 0.
    /// </summary>
    public override int NestingIncrease { get { return 0; } }

    /// <summary>Gets the event type, which allows for simpler type comparisons.</summary>
    internal override EventType Type { get { return EventType.YAML_ALIAS_EVENT; } }

    /// <summary>Gets the value of the alias.</summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchorAlias"/> class.
    /// </summary>
    /// <param name="value">The value of the alias.</param>
    /// <param name="start">The start position of the event.</param>
    /// <param name="end">The end position of the event.</param>
    public AnchorAlias(string value, Mark start, Mark end)
        : base(start, end)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new YamlException(start, end, "Anchor value must not be empty.");
        }

        if (!NodeEvent.AnchorValidator.IsMatch(value))
        {
            throw new YamlException(start, end, "Anchor value must contain alphanumerical characters only.");
        }

        Value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchorAlias"/> class.
    /// </summary>
    /// <param name="value">The value of the alias.</param>
    public AnchorAlias(string value)
        : this(value, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Returns a <see cref="string"/> that represents the current <see cref="object"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="string"/> that represents the current <see cref="object"/>.
    /// </returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"Alias [value = {Value}]");
    }
}