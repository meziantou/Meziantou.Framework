namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// The order of the packets of a JPEG 2000 tile (T.800, B.12): what a reader that stops early gets. The letters are the
/// loops from the outermost one: the quality layer, the resolution level, the component and the position (the precinct).
/// </summary>
public enum Jpeg2000ProgressionOrder
{
    /// <summary>Layer, resolution level, component, position: the image improves in quality, layer after layer.</summary>
    Lrcp = 0,

    /// <summary>Resolution level, layer, component, position: the image grows in resolution, each level with every layer.</summary>
    Rlcp = 1,

    /// <summary>Resolution level, position, component, layer: the image grows in resolution, each area of a level at once.</summary>
    Rpcl = 2,

    /// <summary>Position, component, resolution level, layer: each area of the tile is complete before the next one.</summary>
    Pcrl = 3,

    /// <summary>Component, position, resolution level, layer: each component is complete before the next one.</summary>
    Cprl = 4,
}
