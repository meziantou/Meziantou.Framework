namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A force on the evolution of a Wardley map, drawn as a large arrow with its name: an accelerator points right, a
/// deaccelerator points left (Mermaid <c>accelerator</c> and <c>deaccelerator</c>).
/// </summary>
public sealed class WardleyAccelerator
{
    private string _name;
    private double _visibility;
    private double _evolution;

    /// <summary>Initializes a new instance of the <see cref="WardleyAccelerator"/> class.</summary>
    /// <param name="name">The name, drawn under the arrow.</param>
    /// <param name="visibility">The visibility of the tail of the arrow, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution of the tail of the arrow, between 0 (left) and 1 (right).</param>
    public WardleyAccelerator(string name, double visibility, double evolution)
    {
        _name = WardleyCoordinates.CheckName(name, nameof(name));
        _visibility = WardleyCoordinates.Check(visibility, nameof(visibility));
        _evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets or sets the name, drawn under the arrow.</summary>
    public string Name
    {
        get => _name;
        set => _name = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the visibility of the arrow, between 0 (bottom) and 1 (top).</summary>
    public double Visibility
    {
        get => _visibility;
        set => _visibility = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Gets or sets the evolution of the left end of the arrow, between 0 (left) and 1 (right).</summary>
    public double Evolution
    {
        get => _evolution;
        set => _evolution = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Returns the name.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
