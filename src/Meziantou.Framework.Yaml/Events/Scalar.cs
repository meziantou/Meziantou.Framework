namespace Meziantou.Framework.Yaml.Events;

/// <summary>Represents a scalar event.</summary>
public sealed class Scalar : NodeEvent
{

    /// <summary>
    /// Gets a value indicating the variation of depth caused by this event.
    /// The value can be either -1, 0 or 1. For start events, it will be 1,
    /// for end events, it will be -1, and for the remaining events, it will be 0.
    /// </summary>
    public override int NestingIncrease { get { return 0; } }

    /// <summary>Gets the event type, which allows for simpler type comparisons.</summary>
    internal override EventType Type { get { return EventType.YAML_SCALAR_EVENT; } }

    /// <summary>Gets the value.</summary>
    /// <value>The value.</value>
    public string Value { get; }

    /// <summary>Gets the style of the scalar.</summary>
    /// <value>The style.</value>
    public ScalarStyle Style { get; }

    /// <summary>Gets a value indicating whether the tag is optional for the plain style.</summary>
    public bool IsPlainImplicit { get; }

    /// <summary>Gets a value indicating whether the tag is optional for any non-plain style.</summary>
    public bool IsQuotedImplicit { get; }

    /// <summary>Gets a value indicating whether this instance is canonical.</summary>
    /// <value></value>
    public override bool IsCanonical { get { return !IsPlainImplicit && !IsQuotedImplicit; } }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="value">The value.</param>
    /// <param name="style">The style.</param>
    /// <param name="isPlainImplicit">.</param>
    /// <param name="isQuotedImplicit">.</param>
    /// <param name="start">The start position of the event.</param>
    /// <param name="end">The end position of the event.</param>
    public Scalar(string? anchor, string? tag, string value, ScalarStyle style, bool isPlainImplicit, bool isQuotedImplicit, Mark start, Mark end)
        : base(anchor, tag, start, end)
    {
        Value = value;
        Style = style;
        IsPlainImplicit = isPlainImplicit;
        IsQuotedImplicit = isQuotedImplicit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="value">The value.</param>
    /// <param name="style">The style.</param>
    /// <param name="isPlainImplicit">.</param>
    /// <param name="isQuotedImplicit">.</param>
    public Scalar(string? anchor, string? tag, string value, ScalarStyle style, bool isPlainImplicit, bool isQuotedImplicit)
        : this(anchor, tag, value, style, isPlainImplicit, isQuotedImplicit, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public Scalar(string value)
        : this(null, null, value, ScalarStyle.Any, true, true, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="style">The style of this scalar</param>
    public Scalar(string value, ScalarStyle style)
        : this(null, null, value, style, true, true, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="value">The value.</param>
    public Scalar(string? tag, string value)
        : this(null, tag, value, ScalarStyle.Any, true, true, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scalar"/> class.
    /// </summary>
    public Scalar(string anchor, string tag, string value)
        : this(anchor, tag, value, ScalarStyle.Any, true, true, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Returns a <see cref="System.String"/> that represents the current <see cref="System.Object"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="System.String"/> that represents the current <see cref="System.Object"/>.
    /// </returns>
    public override string ToString()
    {
        return FormattableString.Invariant(
            $"Scalar [anchor = {Anchor}, tag = {Tag}, value = {Value}, style = {Style}, isPlainImplicit = {IsPlainImplicit}, isQuotedImplicit = {IsQuotedImplicit}]");
    }
}