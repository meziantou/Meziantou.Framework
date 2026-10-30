namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The transforms of the main image of a VP8L stream.</summary>
internal enum Vp8LTransformMode
{
    /// <summary>Color indexing, with the indexes of 16 colors or fewer bundled.</summary>
    Palette,

    /// <summary>Color indexing (no bundling), then the predictor transform on the indexes.</summary>
    PredictedPalette,

    /// <summary>Subtract green, the predictor transform, then the color transform (from effort 3).</summary>
    Spatial,

    /// <summary>Subtract green only: the pixels are coded as they are, and repetitions by backward references.</summary>
    SubtractGreen,
}
