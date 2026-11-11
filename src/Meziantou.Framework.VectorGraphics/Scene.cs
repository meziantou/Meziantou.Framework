namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A drawing made of primitives (shapes, paths, texts, groups), independent of the output format. Charts and diagrams
/// create it (<see cref="ISceneSource.CreateScene"/>); <see cref="SvgSceneRenderer"/> and the renderer of
/// <c>Meziantou.Framework.Imaging.VectorGraphics</c> draw it.
/// </summary>
/// <remarks>The coordinates are in pixels, with the origin at the top-left corner and the y axis pointing down. Angles are in degrees, clockwise.</remarks>
public sealed class Scene
{
    /// <summary>Initializes a new instance of the <see cref="Scene"/> class.</summary>
    /// <param name="width">The width of the drawing.</param>
    /// <param name="height">The height of the drawing.</param>
    /// <param name="fontFamily">The CSS <c>font-family</c> of the texts.</param>
    public Scene(double width, double height, string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        Width = width;
        Height = height;
        FontFamily = fontFamily;
    }

    /// <summary>Gets the width of the drawing.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the drawing.</summary>
    public double Height { get; }

    /// <summary>Gets the CSS <c>font-family</c> of the texts.</summary>
    public string FontFamily { get; }

    /// <summary>
    /// Gets or sets the fonts that draw the texts, or <see langword="null"/> to let each renderer choose them. Set it to
    /// the fonts the texts were measured with (<see cref="ITextMeasurer.Fonts"/>), so they are drawn with the same fonts.
    /// </summary>
    /// <remarks>The family of a text is <see cref="SceneFont.Family"/>, or <see cref="FontFamily"/> when it is <see langword="null"/>.</remarks>
    public SceneFontCollection? Fonts { get; set; }

    /// <summary>Gets or sets the accessible name of the drawing, usually its title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the accessible description of the drawing.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the background, or <see langword="null"/> for a transparent background.</summary>
    public SceneColor? Background { get; set; }

    /// <summary>Gets the elements of the drawing, drawn in order.</summary>
    public SceneGroup Root { get; } = new();
}
