namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The outcome of reading the colors of a row for indexed PNG output.</summary>
internal enum PngIndexedRowStatus
{
    /// <summary>Every pixel of the row has a palette entry.</summary>
    Ok,

    /// <summary>A 16-bit sample is not exactly representable in 8 bits.</summary>
    NotEightBit,

    /// <summary>The row has a new color and the palette already holds 256 colors.</summary>
    TooManyColors,

    /// <summary>The row has a color that is not in the final palette.</summary>
    MissingColor,
}
