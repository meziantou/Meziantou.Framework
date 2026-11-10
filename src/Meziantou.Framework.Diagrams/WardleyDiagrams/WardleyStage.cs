namespace Meziantou.Framework.Diagrams;

/// <summary>A stage of the evolution axis of a Wardley map, such as Genesis or Commodity.</summary>
public sealed class WardleyStage
{
    private string _name;
    private double? _boundary;

    /// <summary>Initializes a new instance of the <see cref="WardleyStage"/> class.</summary>
    /// <param name="name">The name of the stage.</param>
    /// <param name="secondName">The second name of the stage, written after a slash, or <see langword="null"/>.</param>
    public WardleyStage(string name, string? secondName = null)
    {
        _name = WardleyCoordinates.CheckName(name, nameof(name));
        SecondName = secondName;
    }

    /// <summary>Gets or sets the name of the stage.</summary>
    public string Name
    {
        get => _name;
        set => _name = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the second name of the stage, drawn after a slash (Mermaid <c>Genesis / Concept</c>), or <see langword="null"/>.</summary>
    public string? SecondName { get; set; }

    /// <summary>
    /// Gets or sets the end of the stage on the evolution axis, between 0 and 1 (Mermaid <c>Genesis@0.25</c>), or
    /// <see langword="null"/>. The boundaries are used when every stage has one; otherwise the stages share the axis equally.
    /// </summary>
    public double? Boundary
    {
        get => _boundary;
        set
        {
            if (value is { } boundary && (!double.IsFinite(boundary) || boundary < 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The boundary of a stage is a finite number, positive or zero.");

            _boundary = value;
        }
    }

    /// <summary>Gets the text drawn under the axis: the name, and the second name after a slash.</summary>
    public string Label => SecondName is null ? WardleyText.Trim(Name) : WardleyText.Trim(Name) + " / " + WardleyText.Trim(SecondName);

    /// <summary>Returns the text of the stage.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Label;
}
