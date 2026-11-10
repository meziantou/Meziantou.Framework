namespace Meziantou.Framework.Diagrams;

/// <summary>A step of a given/when/then scenario of an event model: a kind of entity and the name of a declared entity.</summary>
public sealed class EventModelingStep
{
    /// <summary>Initializes a new instance of the <see cref="EventModelingStep"/> class.</summary>
    /// <param name="kind">The kind of the entity.</param>
    /// <param name="entity">The name of an entity of <see cref="EventModelingDiagram.Entities"/>.</param>
    public EventModelingStep(EventModelingEntityKind kind, string entity)
    {
        ArgumentException.ThrowIfNullOrEmpty(entity);
        Kind = kind;
        Entity = entity;
    }

    /// <summary>Gets or sets the kind of the entity.</summary>
    public EventModelingEntityKind Kind { get; set; }

    /// <summary>Gets or sets the name of the entity.</summary>
    public string Entity { get; set; }
}
