namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What stopped a call to <see cref="Jpeg2000CodestreamWalker.Feed"/>.</summary>
internal enum Jpeg2000CodestreamEvent
{
    /// <summary>More bytes are needed.</summary>
    None,

    /// <summary>The SIZ marker segment was read.</summary>
    ImageSize,

    /// <summary>The main header is complete: the next bytes are those of the first tile-part.</summary>
    MainHeader,

    /// <summary>Every tile-part of a tile was read (only when the walker collects the tiles).</summary>
    Tile,

    /// <summary>The EOC marker was read and every collected tile was handed over.</summary>
    End,
}
