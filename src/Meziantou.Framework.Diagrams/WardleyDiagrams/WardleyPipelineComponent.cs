namespace Meziantou.Framework.Diagrams;

/// <summary>A component of a pipeline of a Wardley map: a variant of the parent component, at the visibility of the parent.</summary>
public sealed class WardleyPipelineComponent
{
    private string _name;
    private double _evolution;

    /// <summary>Initializes a new instance of the <see cref="WardleyPipelineComponent"/> class.</summary>
    /// <param name="name">The name of the component, which the links use.</param>
    /// <param name="evolution">The evolution, between 0 (left) and 1 (right).</param>
    public WardleyPipelineComponent(string name, double evolution)
    {
        _name = WardleyCoordinates.CheckName(name, nameof(name));
        _evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets or sets the name of the component, which the links use.</summary>
    public string Name
    {
        get => _name;
        set => _name = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the evolution, between 0 (left) and 1 (right).</summary>
    public double Evolution
    {
        get => _evolution;
        set => _evolution = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Gets or sets the position of the label relative to the component, or <see langword="null"/> for the default.</summary>
    public WardleyLabelOffset? LabelOffset { get; set; }

    /// <summary>Returns the name of the component.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
