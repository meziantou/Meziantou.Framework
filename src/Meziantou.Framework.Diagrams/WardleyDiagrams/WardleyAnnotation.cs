namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A numbered annotation of a Wardley map: a number in a circle at a position, whose text is listed in the box of the
/// annotations (Mermaid <c>annotation 1,[visibility, evolution] "text"</c>).
/// </summary>
public sealed class WardleyAnnotation
{
    private int _number;
    private string _text;
    private double _visibility;
    private double _evolution;

    /// <summary>Initializes a new instance of the <see cref="WardleyAnnotation"/> class.</summary>
    /// <param name="number">The number, positive or zero.</param>
    /// <param name="text">The text, listed in the box of the annotations. An empty text is not listed.</param>
    /// <param name="visibility">The visibility of the circle, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution of the circle, between 0 (left) and 1 (right).</param>
    public WardleyAnnotation(int number, string text, double visibility, double evolution)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        _number = number;
        _text = WardleyCoordinates.CheckName(text, nameof(text));
        _visibility = WardleyCoordinates.Check(visibility, nameof(visibility));
        _evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets or sets the number, positive or zero.</summary>
    public int Number
    {
        get => _number;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _number = value;
        }
    }

    /// <summary>Gets or sets the text, listed in the box of the annotations. An empty text is not listed.</summary>
    public string Text
    {
        get => _text;
        set => _text = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the visibility of the circle, between 0 (bottom) and 1 (top).</summary>
    public double Visibility
    {
        get => _visibility;
        set => _visibility = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Gets or sets the evolution of the circle, between 0 (left) and 1 (right).</summary>
    public double Evolution
    {
        get => _evolution;
        set => _evolution = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Returns the number and the text of the annotation.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + Text;
}
