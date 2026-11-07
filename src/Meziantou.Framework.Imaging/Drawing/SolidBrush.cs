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

    /// <summary>Creates a paint whose sample range and precision are preserved on floating-point canvases.</summary>
    /// <param name="color">The color, with straight alpha. Integer canvases use its normalized 16-bit representation.</param>
    public SolidBrush(RgbaFloat color)
    {
        FloatColor = color;
        Color = new Rgba64(ToSample(color.R), ToSample(color.G), ToSample(color.B), ToSample(color.A));
    }

    internal RgbaFloat? FloatColor { get; }

    private static ushort ToSample(float value) => float.IsNaN(value) ? (ushort)0 : (ushort)Math.Round(Math.Clamp((double)value, 0, 1) * ushort.MaxValue, MidpointRounding.AwayFromZero);

    /// <summary>Gets the color, with straight alpha.</summary>
    public Rgba64 Color { get; }
}
