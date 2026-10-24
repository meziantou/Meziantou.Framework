namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// The attributes of a group (<see cref="DrawingCanvas.Group(GroupOptions, DrawingAction, DrawingAction?)"/>): a set of
/// drawings composited as a whole, which can mix with what is under it (a non-isolated group) and whose drawings can
/// knock out one another (a knockout group), as the transparency groups of PDF (ISO 32000-2, 11.4) and the groups of
/// the W3C Compositing and Blending specification.
/// </summary>
public sealed class GroupOptions
{
    /// <summary>Gets the opacity of the group, from 0 to 1: it multiplies the alpha of the group. The default is 1.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Gets the blend mode that mixes the colors of the group with those under it. The default is <see cref="BlendMode.Normal"/>.</summary>
    public BlendMode BlendMode { get; init; }

    /// <summary>
    /// Gets a value indicating whether the group is isolated: its drawings start on a transparent backdrop, as the
    /// drawings of a layer. A non-isolated group starts with the pixels under it, which its blend modes mix with; the
    /// pixels under it are then removed from its result before it is composited, so that they are not counted twice.
    /// The default is <see langword="true"/>.
    /// </summary>
    public bool IsIsolated { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the group is a knockout group: each element drawn with
    /// <see cref="DrawingCanvas.KnockoutElement(double, DrawingAction, DrawingAction?, MaskMode)"/> on the canvas of the
    /// group is composited with the initial backdrop of the group, and replaces the elements drawn before it in
    /// proportion to its shape. The default is <see langword="false"/>.
    /// </summary>
    public bool IsKnockout { get; init; }

    /// <summary>Gets how the values of the mask of the group are read: its alpha, or its luminance. The default is <see cref="MaskMode.Alpha"/>.</summary>
    public MaskMode MaskMode { get; init; } = MaskMode.Alpha;

    /// <summary>Gets the rectangle the group can change, in the coordinates of the drawing, or <see langword="null"/> (the default) for the bounds of the clip.</summary>
    public RectangleD? Bounds { get; init; }
}
