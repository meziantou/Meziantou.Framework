namespace Meziantou.Framework.Charts;

/// <summary>What a mark needs from the scales and the layout of its plot.</summary>
[Flags]
internal enum MarkTraits
{
    None = 0,

    /// <summary>The x scale is a band scale.</summary>
    BandX = 1 << 0,

    /// <summary>The y scale is a band scale.</summary>
    BandY = 1 << 1,

    /// <summary>The x scale includes zero.</summary>
    ZeroX = 1 << 2,

    /// <summary>The y scale includes zero.</summary>
    ZeroY = 1 << 3,

    /// <summary>The plot uses polar coordinates when it does not set its coordinates.</summary>
    PreferPolar = 1 << 4,

    /// <summary>The mark is laid out in the plot area, without the position scales.</summary>
    Frame = 1 << 5,

    /// <summary>The x axis is hidden by default.</summary>
    HideXAxis = 1 << 6,

    /// <summary>The y axis is hidden by default.</summary>
    HideYAxis = 1 << 7,

    /// <summary>The domain of the x scale is not extended to round values.</summary>
    ExactX = 1 << 8,

    /// <summary>The domain of the y scale is not extended to round values.</summary>
    ExactY = 1 << 9,
}
