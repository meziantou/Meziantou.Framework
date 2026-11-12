namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The order in which a shape or a text paints its fill, its stroke and its markers, as the SVG <c>paint-order</c>
/// property (<see cref="SceneShape.PaintOrder"/>). What is painted later is over what is painted before.
/// </summary>
public enum ScenePaintOrder
{
    /// <summary>The fill, then the stroke, then the markers: the default of SVG (<c>normal</c>).</summary>
    FillStrokeMarkers,

    /// <summary>The fill, then the markers, then the stroke (<c>fill markers stroke</c>).</summary>
    FillMarkersStroke,

    /// <summary>The stroke, then the fill, then the markers (<c>stroke</c>): the fill covers the inner half of the stroke, as for an outlined text.</summary>
    StrokeFillMarkers,

    /// <summary>The stroke, then the markers, then the fill (<c>stroke markers fill</c>).</summary>
    StrokeMarkersFill,

    /// <summary>The markers, then the fill, then the stroke (<c>markers</c>).</summary>
    MarkersFillStroke,

    /// <summary>The markers, then the stroke, then the fill (<c>markers stroke fill</c>).</summary>
    MarkersStrokeFill,
}
