namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The <c>TUPLTYPE</c> of a PAM file, or the equivalent tuple of a PBM/PGM/PPM file.</summary>
internal enum PnmTupleType
{
    /// <summary><c>BLACKANDWHITE</c>: one sample, <c>MAXVAL</c> 1, 0 is black (PAM only; a PBM bit is inverted).</summary>
    BlackAndWhite = 0,

    /// <summary><c>GRAYSCALE</c>: one sample.</summary>
    Grayscale = 1,

    /// <summary><c>RGB</c>: three samples.</summary>
    Rgb = 2,

    /// <summary><c>BLACKANDWHITE_ALPHA</c>: two samples, <c>MAXVAL</c> 1.</summary>
    BlackAndWhiteAlpha = 3,

    /// <summary><c>GRAYSCALE_ALPHA</c>: two samples.</summary>
    GrayscaleAlpha = 4,

    /// <summary><c>RGB_ALPHA</c>: four samples.</summary>
    RgbAlpha = 5,
}
