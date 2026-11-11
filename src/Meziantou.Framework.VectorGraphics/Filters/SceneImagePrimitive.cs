namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive whose result is the drawing of an element: <c>feImage</c> of SVG, of an image or of an element.</summary>
/// <remarks>
/// The element is drawn in the coordinates of the filter (the parent of the filtered element), with its own transform
/// and effects, and cut to the subregion of the primitive (by default, the region of the filter). The scene draws it
/// only there: it does not need to be in the scene otherwise. A primitive whose element draws, through its own
/// filters, the element that the primitive filters draws nothing there.
/// </remarks>
/// <param name="content">The element: a <see cref="SceneImage"/>, or any element.</param>
/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
public sealed class SceneImagePrimitive(SceneElement content) : SceneFilterPrimitive
{
    /// <summary>Gets the element whose drawing is the result.</summary>
    public SceneElement Content { get; } = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>
    /// Gets a value indicating whether the element is an image without effects that fills the subregion of the
    /// primitive: SVG writes its data in the <c>feImage</c> itself, rather than a reference to an element, which some
    /// viewers do not draw.
    /// </summary>
    internal bool IsInlineImage => Content is SceneImage { Transform: null, Filter: null, Mask: null, ClippingPath: null, BlendMode: SceneBlendMode.Normal, Opacity: >= 1 } image
        && (Subregion is null || Subregion == new SceneBounds(image.X, image.Y, image.Width, image.Height));

    internal override IEnumerable<SceneFilterInput> GetInputs() => [];
}
