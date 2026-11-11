namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The view box of a group (<see cref="SceneGroup.ViewBox"/>), as a nested SVG <c>svg</c> element: a rectangle of the
/// coordinates of the children, mapped to a viewport of the coordinates of the group, which cuts what is outside of it.
/// </summary>
/// <param name="Box">The rectangle of the coordinates of the children that is shown, as the SVG <c>viewBox</c>.</param>
/// <param name="Viewport">The rectangle it is shown in, in the coordinates of the group (inside its <see cref="SceneElement.Transform"/>).</param>
public sealed record SceneViewBox(SceneBounds Box, SceneBounds Viewport)
{
    /// <summary>Gets how the box keeps its proportions in the viewport. The default is <see cref="SceneAspectRatio.XMidYMid"/>, as in SVG.</summary>
    public SceneAspectRatio Alignment { get; init; } = SceneAspectRatio.XMidYMid;

    /// <summary>
    /// Gets a value that indicates whether the box covers the whole viewport and is cut (<c>slice</c>), rather than
    /// fitting in it (<c>meet</c>, the default). It does not apply when <see cref="Alignment"/> is <see cref="SceneAspectRatio.None"/>.
    /// </summary>
    public bool Slice { get; init; }

    /// <summary>Gets the transform from the coordinates of the children to the coordinates of the group, as SVG computes it.</summary>
    /// <returns>The transform, or <see langword="null"/> when the box or the viewport has no area or a coordinate that is not finite: nothing is drawn.</returns>
    public SceneTransform? GetTransform()
    {
        if (!IsDrawable(Box) || !IsDrawable(Viewport))
            return null;

        var scaleX = Viewport.Width / Box.Width;
        var scaleY = Viewport.Height / Box.Height;
        if (Alignment is not SceneAspectRatio.None)
        {
            scaleX = scaleY = Slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        }

        var (alignX, alignY) = Alignment switch
        {
            SceneAspectRatio.XMinYMin => (0.0, 0.0),
            SceneAspectRatio.XMidYMin => (0.5, 0.0),
            SceneAspectRatio.XMaxYMin => (1.0, 0.0),
            SceneAspectRatio.XMinYMid => (0.0, 0.5),
            SceneAspectRatio.XMaxYMid => (1.0, 0.5),
            SceneAspectRatio.XMinYMax => (0.0, 1.0),
            SceneAspectRatio.XMidYMax => (0.5, 1.0),
            SceneAspectRatio.XMaxYMax => (1.0, 1.0),
            _ => (0.5, 0.5),
        };

        var offsetX = Viewport.X - (Box.X * scaleX) + (alignX * (Viewport.Width - (Box.Width * scaleX)));
        var offsetY = Viewport.Y - (Box.Y * scaleY) + (alignY * (Viewport.Height - (Box.Height * scaleY)));
        var transform = new SceneTransform(scaleX, 0, 0, scaleY, offsetX, offsetY);
        return double.IsFinite(scaleX) && double.IsFinite(scaleY) && double.IsFinite(offsetX) && double.IsFinite(offsetY) ? transform : null;

        static bool IsDrawable(SceneBounds bounds) => bounds.Width > 0 && bounds.Height > 0 && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height);
    }
}
