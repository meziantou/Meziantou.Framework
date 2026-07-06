namespace Meziantou.Framework.Yaml.Events;

/// <summary>Represents a sequence start event.</summary>
public sealed class SequenceStart : NodeEvent
{
    /// <summary>
    /// Gets a value indicating the variation of depth caused by this event.
    /// The value can be either -1, 0 or 1. For start events, it will be 1,
    /// for end events, it will be -1, and for the remaining events, it will be 0.
    /// </summary>
    public override int NestingIncrease { get { return 1; } }

    /// <summary>Gets the event type, which allows for simpler type comparisons.</summary>
    internal override EventType Type { get { return EventType.YAML_SEQUENCE_START_EVENT; } }

    /// <summary>Gets a value indicating whether this instance is implicit.</summary>
    /// <value>
    /// 	<c>true</c> if this instance is implicit; otherwise, <c>false</c>.
    /// </value>
    public bool IsImplicit { get; }

    /// <summary>Gets a value indicating whether this instance is canonical.</summary>
    /// <value></value>
    public override bool IsCanonical { get { return !IsImplicit; } }

    /// <summary>Gets the style.</summary>
    /// <value>The style.</value>
    public YamlStyle Style { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SequenceStart"/> class.
    /// </summary>
    public SequenceStart() : base(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SequenceStart"/> class.
    /// </summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="isImplicit">if set to <c>true</c> [is implicit].</param>
    /// <param name="style">The style.</param>
    /// <param name="start">The start position of the event.</param>
    /// <param name="end">The end position of the event.</param>
    public SequenceStart(string? anchor, string? tag, bool isImplicit, YamlStyle style, Mark start, Mark end)
        : base(anchor, tag, start, end)
    {
        IsImplicit = isImplicit;
        Style = style;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SequenceStart"/> class.
    /// </summary>
    public SequenceStart(string? anchor, string? tag, bool isImplicit, YamlStyle style)
        : this(anchor, tag, isImplicit, style, Mark.Empty, Mark.Empty)
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
        return FormattableString.Invariant(
            $"Sequence start [anchor = {Anchor}, tag = {Tag}, isImplicit = {IsImplicit}, style = {Style}]");
    }
}