namespace Meziantou.Framework.Diagrams;

/// <summary>A movement of a component of a Wardley map toward a later stage of evolution, drawn as a dashed red arrow (Mermaid <c>evolve</c>).</summary>
public sealed class WardleyEvolve
{
    private string _component;
    private double _evolution;

    /// <summary>Initializes a new instance of the <see cref="WardleyEvolve"/> class.</summary>
    /// <param name="component">The name of the component that evolves.</param>
    /// <param name="evolution">The evolution the component moves to, between 0 (left) and 1 (right).</param>
    public WardleyEvolve(string component, double evolution)
    {
        _component = WardleyCoordinates.CheckName(component, nameof(component));
        _evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets or sets the name of the component that evolves.</summary>
    public string Component
    {
        get => _component;
        set => _component = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the evolution the component moves to, between 0 (left) and 1 (right).</summary>
    public double Evolution
    {
        get => _evolution;
        set => _evolution = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Returns the component and its target.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Component + " -> " + Evolution.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
