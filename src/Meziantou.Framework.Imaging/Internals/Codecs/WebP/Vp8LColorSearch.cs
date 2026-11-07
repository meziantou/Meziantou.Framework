namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the color transform chooses the multipliers of each tile.</summary>
internal enum Vp8LColorSearch
{
    /// <summary>No color transform.</summary>
    None,

    /// <summary>Each multiplier by the entropy of the transformed channel in the tile, among about 40 values.</summary>
    Entropy,

    /// <summary>
    /// The multipliers estimated by least squares, their neighbors, zero and those of the left and top tiles, by the cost of the
    /// transformed channels under the statistics of the tiles chosen before.
    /// </summary>
    Cost,
}
