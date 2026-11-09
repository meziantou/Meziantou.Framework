namespace Meziantou.Framework.Diagrams;

/// <summary>A note of a Wardley map: a bold text at a position (Mermaid <c>note "text" [visibility, evolution]</c>).</summary>
public sealed class WardleyNote
{
    private string _text;
    private double _visibility;
    private double _evolution;

    /// <summary>Initializes a new instance of the <see cref="WardleyNote"/> class.</summary>
    /// <param name="text">The text of the note.</param>
    /// <param name="visibility">The visibility of its start, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution of its start, between 0 (left) and 1 (right).</param>
    public WardleyNote(string text, double visibility, double evolution)
    {
        _text = WardleyCoordinates.CheckName(text, nameof(text));
        _visibility = WardleyCoordinates.Check(visibility, nameof(visibility));
        _evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets or sets the text of the note.</summary>
    public string Text
    {
        get => _text;
        set => _text = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the visibility of the start of the text, between 0 (bottom) and 1 (top).</summary>
    public double Visibility
    {
        get => _visibility;
        set => _visibility = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Gets or sets the evolution of the start of the text, between 0 (left) and 1 (right).</summary>
    public double Evolution
    {
        get => _evolution;
        set => _evolution = WardleyCoordinates.Check(value, nameof(value));
    }

    /// <summary>Returns the text of the note.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text;
}
