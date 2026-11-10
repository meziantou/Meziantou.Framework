namespace Meziantou.Framework.Diagrams;

/// <summary>A component of a Wardley map: a part of the value chain, drawn as a circle with its name (Mermaid <c>component</c>).</summary>
public sealed class WardleyComponent
{
    private string _name;
    private double _visibility;
    private double _evolution;

    /// <summary>Initializes a new instance of the <see cref="WardleyComponent"/> class.</summary>
    /// <param name="name">The name of the component, which the links use.</param>
    /// <param name="visibility">The visibility, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution, between 0 (left) and 1 (right).</param>
    public WardleyComponent(string name, double visibility, double evolution)
    {
        _name = WardleyCoordinates.CheckName(name, nameof(name));
        _visibility = WardleyCoordinates.Check(visibility, nameof(visibility));
        _evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets or sets the name of the component, which the links use.</summary>
    public string Name
    {
        get => _name;
        set => _name = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the visibility: how close the component is to the user, between 0 (bottom) and 1 (top).</summary>
    public double Visibility
    {
        get => _visibility;
        set => _visibility = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Gets or sets the evolution: how mature the component is, between 0 (genesis, left) and 1 (commodity, right).</summary>
    public double Evolution
    {
        get => _evolution;
        set => _evolution = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Gets or sets the position of the label relative to the component, or <see langword="null"/> for the default, above and to the right.</summary>
    public WardleyLabelOffset? LabelOffset { get; set; }

    /// <summary>Gets or sets how the component is sourced, drawn as a larger circle around it.</summary>
    public WardleySourcing Sourcing { get; set; }

    /// <summary>Gets or sets a value that indicates whether the component resists change, drawn as a thick bar on its right (Mermaid <c>(inertia)</c>).</summary>
    public bool Inertia { get; set; }

    /// <summary>Returns the name of the component.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
