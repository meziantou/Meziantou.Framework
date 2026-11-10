namespace Meziantou.Framework.Diagrams;

/// <summary>A frame of an event model: an entity at a point of the timeline, drawn as a box in its swimlane.</summary>
public sealed class EventModelingFrame
{
    /// <summary>Initializes a new instance of the <see cref="EventModelingFrame"/> class.</summary>
    /// <param name="id">The identifier: one to three digits, unique in the diagram.</param>
    /// <param name="kind">The kind of the entity.</param>
    /// <param name="entity">The name of the entity, such as <c>AddItem</c>, or <c>Cart.AddItem</c> to put it in the swimlane of the namespace <c>Cart</c>.</param>
    public EventModelingFrame(string id, EventModelingEntityKind kind, string entity)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(entity);
        Id = id;
        Kind = kind;
        Entity = entity;
    }

    /// <summary>Gets the identifier of the frame, unique in the diagram.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the kind of the entity, which sets the swimlane and the colors of the box.</summary>
    public EventModelingEntityKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the name of the entity. A name of two parts, <c>Namespace.Name</c>, puts the box in the swimlane of the
    /// namespace and shows the second part.
    /// </summary>
    public string Entity { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the frame is a reset frame (Mermaid <c>rf</c>): no relation is inferred
    /// from the frames before it.
    /// </summary>
    public bool IsReset { get; set; }

    /// <summary>
    /// Gets the frames this frame receives input from (Mermaid <c>-&gt;&gt;</c>). Without sources, a relation is inferred from
    /// the last frame of another swimlane before it, unless the frame is a reset frame.
    /// </summary>
    public IList<EventModelingFrame> Sources { get; } = new List<EventModelingFrame>();

    /// <summary>Gets or sets the data block whose data the box shows, or <see langword="null"/> for none. It takes precedence over <see cref="Data"/>.</summary>
    public EventModelingDataBlock? DataBlock { get; set; }

    /// <summary>Gets or sets the inline data the box shows, or <see langword="null"/> for none. It is on one line.</summary>
    public EventModelingData? Data { get; set; }

    /// <summary>Returns the identifier and the entity of the frame.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Id + " " + Entity;
}
