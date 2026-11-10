namespace Meziantou.Framework.Diagrams;

/// <summary>A data block of an event model: named data that frames reference (Mermaid <c>data name { ... }</c>).</summary>
public sealed class EventModelingDataBlock
{
    /// <summary>Initializes a new instance of the <see cref="EventModelingDataBlock"/> class.</summary>
    /// <param name="name">The name, which frames use to reference the block.</param>
    /// <param name="data">The data.</param>
    public EventModelingDataBlock(string name, EventModelingData data)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(data);
        Name = name;
        Data = data;
    }

    /// <summary>Gets the name, which frames use to reference the block.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the data.</summary>
    public EventModelingData Data { get; set; }

    /// <summary>Returns the name of the block.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
