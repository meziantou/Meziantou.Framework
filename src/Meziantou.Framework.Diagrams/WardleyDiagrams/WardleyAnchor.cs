namespace Meziantou.Framework.Diagrams;

/// <summary>An anchor of a Wardley map: a user or a customer, whose needs the value chain serves, drawn as a bold name (Mermaid <c>anchor</c>).</summary>
public sealed class WardleyAnchor
{
    private string _name;
    private double _visibility;
    private double _evolution;

    /// <summary>Initializes a new instance of the <see cref="WardleyAnchor"/> class.</summary>
    /// <param name="name">The name of the anchor, which the links use.</param>
    /// <param name="visibility">The visibility, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution, between 0 (left) and 1 (right).</param>
    public WardleyAnchor(string name, double visibility, double evolution)
    {
        _name = WardleyCoordinates.CheckName(name, nameof(name));
        _visibility = WardleyCoordinates.Check(visibility, nameof(visibility));
        _evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets or sets the name of the anchor, which the links use.</summary>
    public string Name
    {
        get => _name;
        set => _name = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the visibility, between 0 (bottom) and 1 (top).</summary>
    public double Visibility
    {
        get => _visibility;
        set => _visibility = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Gets or sets the evolution, between 0 (left) and 1 (right).</summary>
    public double Evolution
    {
        get => _evolution;
        set => _evolution = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Returns the name of the anchor.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
