using Meziantou.Framework.Yaml.Tokens;

namespace Meziantou.Framework.Yaml.Events;

/// <summary>Represents a document start event.</summary>
public sealed class DocumentStart : ParsingEvent
{
    /// <summary>
    /// Gets a value indicating the variation of depth caused by this event.
    /// The value can be either -1, 0 or 1. For start events, it will be 1,
    /// for end events, it will be -1, and for the remaining events, it will be 0.
    /// </summary>
    public override int NestingIncrease { get { return 1; } }

    /// <summary>Gets the event type, which allows for simpler type comparisons.</summary>
    internal override EventType Type { get { return EventType.YAML_DOCUMENT_START_EVENT; } }

    /// <summary>Gets the tags.</summary>
    /// <value>The tags.</value>
    public TagDirectiveCollection? Tags { get; }

    /// <summary>Gets the version.</summary>
    /// <value>The version.</value>
    public VersionDirective? Version { get; }

    /// <summary>Gets a value indicating whether this instance is implicit.</summary>
    /// <value>
    /// 	<c>true</c> if this instance is implicit; otherwise, <c>false</c>.
    /// </value>
    public bool IsImplicit { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStart"/> class.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="tags">The tags.</param>
    /// <param name="isImplicit">Indicates whether the event is implicit.</param>
    /// <param name="start">The start position of the event.</param>
    /// <param name="end">The end position of the event.</param>
    public DocumentStart(VersionDirective? version, TagDirectiveCollection? tags, bool isImplicit, Mark start, Mark end)
        : base(start, end)
    {
        Version = version;
        Tags = tags;
        IsImplicit = isImplicit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStart"/> class.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="tags">The tags.</param>
    /// <param name="isImplicit">Indicates whether the event is implicit.</param>
    public DocumentStart(VersionDirective? version, TagDirectiveCollection? tags, bool isImplicit)
        : this(version, tags, isImplicit, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStart"/> class.
    /// </summary>
    /// <param name="start">The start position of the event.</param>
    /// <param name="end">The end position of the event.</param>
    public DocumentStart(Mark start, Mark end)
        : this(null, null, true, start, end)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStart"/> class.
    /// </summary>
    public DocumentStart()
        : this(null, null, true, Mark.Empty, Mark.Empty)
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
        return FormattableString.Invariant($"Document start [isImplicit = {IsImplicit}]");
    }
}