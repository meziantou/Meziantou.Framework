namespace Meziantou.Framework.Diagrams;

/// <summary>An entity of an entity relationship diagram: a box with its name and the table of its attributes.</summary>
public sealed class ErEntity : ErElement
{
    /// <summary>Initializes a new instance of the <see cref="ErEntity"/> class.</summary>
    /// <param name="id">The name of the entity, which is shown when there is no label.</param>
    /// <param name="label">The name shown instead of the identifier (Mermaid alias), or <see langword="null"/>.</param>
    public ErEntity(string id, DiagramText? label = null)
        : base(id)
    {
        Label = label;
    }

    /// <summary>Gets the attributes, drawn in order.</summary>
    public IList<ErAttribute> Attributes { get; } = new List<ErAttribute>();

    /// <summary>Adds an attribute.</summary>
    /// <param name="type">The type, such as <c>string</c> or <c>int</c>.</param>
    /// <param name="name">The name.</param>
    /// <param name="keys">The keys the attribute is part of.</param>
    /// <param name="comment">The comment, or <see langword="null"/> for none.</param>
    /// <returns>The attribute.</returns>
    public ErAttribute AddAttribute(string type, string name, ErAttributeKeys keys = ErAttributeKeys.None, string? comment = null)
    {
        var attribute = new ErAttribute(type, name) { Keys = keys, Comment = comment };
        Attributes.Add(attribute);
        return attribute;
    }
}
