namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// An opacity mask (<see cref="SceneElement.Mask"/>): the element is drawn where its content is, with the opacity its
/// content gives, as the <c>mask</c> of SVG. Outside of the content, nothing is drawn.
/// </summary>
public sealed class SceneMask
{
    /// <summary>Gets the content of the mask, in the coordinates of the parent of the masked element.</summary>
    public SceneGroup Content { get; } = new();

    /// <summary>Gets or sets what the content gives the opacity by. The default is <see cref="SceneMaskMode.Luminance"/>, as in SVG.</summary>
    public SceneMaskMode Mode { get; set; }
}
