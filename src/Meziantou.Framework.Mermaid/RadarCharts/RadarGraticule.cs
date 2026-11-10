namespace Meziantou.Framework.Mermaid;

/// <summary>The shape of the scale of a radar chart, and of its curves (Mermaid <c>graticule</c>).</summary>
public enum RadarGraticule
{
    /// <summary>Concentric circles, and curves rounded through their points.</summary>
    Circle,

    /// <summary>Concentric polygons, and curves made of straight lines between their points.</summary>
    Polygon,
}
