namespace Meziantou.Framework.Charts;

/// <summary>An element that is filled or stroked.</summary>
public abstract class SceneShape : SceneElement
{
    private protected SceneShape()
    {
    }

    /// <summary>Gets or sets how the interior is painted, or <see langword="null"/> to leave it empty.</summary>
    public ScenePaint? Fill { get; set; }

    /// <summary>Gets or sets how the outline is painted, or <see langword="null"/> for no outline.</summary>
    public ScenePaint? Stroke { get; set; }

    /// <summary>Gets or sets the width of the outline.</summary>
    public double StrokeWidth { get; set; } = 1;

    /// <summary>Gets or sets the lengths of the dashes and gaps of the outline, or <see langword="null"/> for a solid outline.</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; set; }

    /// <summary>Gets or sets the shape of the ends of open outlines.</summary>
    public SceneLineCap LineCap { get; set; }

    /// <summary>Gets or sets the shape of the corners of outlines.</summary>
    public SceneLineJoin LineJoin { get; set; }
}
