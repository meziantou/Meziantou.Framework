namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the predictor transform chooses the mode of each tile.</summary>
internal enum Vp8LPredictorSearch
{
    /// <summary>The select predictor (mode 11) everywhere.</summary>
    Select,

    /// <summary>The best of 4 modes (select, left, top, gradient) by the sum of the magnitudes of the residuals.</summary>
    FourModes,

    /// <summary>The best of the 14 modes by the sum of the magnitudes of the residuals (or their entropy in the tile, see <see cref="Vp8LConfiguration.EntropyPredictors"/>).</summary>
    AllModes,

    /// <summary>The best of the 14 modes by the cost of the residuals under the statistics of the tiles chosen before.</summary>
    AllModesByCost,
}
