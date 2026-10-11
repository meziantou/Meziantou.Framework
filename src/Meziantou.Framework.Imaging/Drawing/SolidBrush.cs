namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A brush painting a single color.</summary>
public sealed class SolidBrush : Brush
{
    /// <summary>Initializes a new instance of the <see cref="SolidBrush"/> class.</summary>
    /// <param name="color">The color, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    public SolidBrush(Rgba64 color)
    {
        Color = color;
    }

    /// <summary>Gets the color, with straight alpha.</summary>
    public Rgba64 Color { get; }
}
