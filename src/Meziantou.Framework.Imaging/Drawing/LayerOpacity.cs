namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>How the opacity of a layer combines with its operator.</summary>
/// <remarks>See <see cref="DrawingCanvas.Layer(double, DrawingAction, CompositeMode, BlendMode, LayerOpacity)"/>.</remarks>
public enum LayerOpacity
{
    /// <summary>
    /// The opacity is a coverage of what the operator makes, as the edge of a shape is: a pixel gets
    /// <c>o R + (1 - o) D</c>, where <c>R</c> is the layer combined with the canvas <c>D</c>. This is the default.
    /// </summary>
    Coverage = 0,

    /// <summary>
    /// The group opacity of CSS: the alpha of every pixel of the layer is multiplied by the opacity, then the layer is
    /// combined with the canvas by the operator. With <see cref="CompositeMode.Plus"/>, opaque white at 0.6 over an
    /// alpha of 0.6 gives 1; with <see cref="CompositeMode.DestinationIn"/>, a mask at half its opacity keeps half of
    /// what it covers and nothing of what it does not cover. With <see cref="CompositeMode.SourceOver"/>, it is
    /// <see cref="Coverage"/>, pixel for pixel.
    /// </summary>
    Group = 1,
}
