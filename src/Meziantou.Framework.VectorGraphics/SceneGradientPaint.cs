namespace Meziantou.Framework.VectorGraphics;

/// <summary>A gradient: <see cref="LinearGradientPaint"/> or <see cref="RadialGradientPaint"/>.</summary>
public abstract class SceneGradientPaint : ScenePaint
{
    private protected SceneGradientPaint(IEnumerable<SceneGradientStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        Stops = [.. stops];
    }

    /// <summary>Gets the color stops, by increasing offset.</summary>
    public IReadOnlyList<SceneGradientStop> Stops { get; }

    /// <summary>Gets how the gradient paints beyond its ends. The default is <see cref="SceneGradientSpread.Pad"/>.</summary>
    public SceneGradientSpread Spread { get; init; }

    /// <summary>
    /// Gets the transform from the coordinates of the gradient to the coordinates of the shape it paints, as the
    /// <c>gradientTransform</c> of SVG, or <see langword="null"/> for none: it skews, rotates or stretches a gradient,
    /// such as a radial gradient into an ellipse.
    /// </summary>
    public SceneTransform? Transform { get; init; }
}
