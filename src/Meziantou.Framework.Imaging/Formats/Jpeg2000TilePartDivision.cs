namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Where a JPEG 2000 tile is cut into tile-parts (T.800, A.4.2): each tile-part holds packets that follow each other in the
/// progression order, so that a reader or a server can find the start of a resolution level, of a layer or of a component
/// without reading the packets.
/// </summary>
public enum Jpeg2000TilePartDivision
{
    /// <summary>One tile-part per tile.</summary>
    None = 0,

    /// <summary>A tile-part starts at each packet whose resolution level is not the one of the previous packet.</summary>
    Resolution = 1,

    /// <summary>A tile-part starts at each packet whose quality layer is not the one of the previous packet.</summary>
    Layer = 2,

    /// <summary>A tile-part starts at each packet whose component is not the one of the previous packet.</summary>
    Component = 3,
}
