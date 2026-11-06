namespace Meziantou.Framework.Charts;

/// <summary>A line of text, positioned by its baseline.</summary>
/// <param name="x">The x coordinate of the anchor.</param>
/// <param name="y">The y coordinate of the baseline.</param>
/// <param name="text">The text.</param>
/// <param name="font">The size and the weight of the font.</param>
/// <param name="color">The color of the text.</param>
public sealed class SceneText(double x, double y, string text, ChartFont font, ChartColor color) : SceneElement
{
    /// <summary>Gets the x coordinate of the anchor.</summary>
    public double X { get; } = x;

    /// <summary>Gets the y coordinate of the baseline.</summary>
    public double Y { get; } = y;

    /// <summary>Gets the text.</summary>
    public string Text { get; } = text ?? throw new ArgumentNullException(nameof(text));

    /// <summary>Gets the size and the weight of the font.</summary>
    public ChartFont Font { get; } = font;

    /// <summary>Gets the color of the text.</summary>
    public ChartColor Color { get; } = color;

    /// <summary>Gets or sets where the text is anchored horizontally.</summary>
    public SceneTextAnchor Anchor { get; set; }

    /// <summary>Gets or sets the rotation of the text around its anchor, in degrees, clockwise.</summary>
    public double Rotation { get; set; }

    /// <summary>Gets or sets the advance width of the text, as the layout measured it.</summary>
    public double MeasuredWidth { get; set; }
}
