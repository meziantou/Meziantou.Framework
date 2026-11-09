namespace Meziantou.Framework.Diagrams;

/// <summary>How a component of a Wardley map is sourced, drawn as a larger circle around it (Mermaid <c>(build)</c>, <c>(buy)</c>...).</summary>
public enum WardleySourcing
{
    /// <summary>No sourcing is given.</summary>
    None,

    /// <summary>The component is built in house: a very light gray circle.</summary>
    Build,

    /// <summary>The component is bought: a light gray circle.</summary>
    Buy,

    /// <summary>The component is outsourced: a dark gray circle.</summary>
    Outsource,

    /// <summary>The component comes from a market: a circle with three small circles in a triangle.</summary>
    Market,
}
