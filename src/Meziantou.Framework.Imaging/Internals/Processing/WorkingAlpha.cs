namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the alpha sample of a pixel takes part in <see cref="WorkingSamples"/>.</summary>
internal enum WorkingAlpha
{
    /// <summary>The format has no alpha: every sample is a color.</summary>
    None = 0,

    /// <summary>The last of four samples is a straight alpha: colors are filtered premultiplied and alpha is filtered too.</summary>
    Premultiplied,

    /// <summary>The last of four samples is a straight alpha: colors are filtered straight and the stored alpha is kept.</summary>
    Preserved,

    /// <summary>
    /// The last of four stored samples is an alpha at its maximum in every pixel the operation reads: the working samples are
    /// the three colors only (premultiplying them by a constant alpha only adds rounding), and the stored alpha is the
    /// maximum.
    /// </summary>
    Opaque,
}
