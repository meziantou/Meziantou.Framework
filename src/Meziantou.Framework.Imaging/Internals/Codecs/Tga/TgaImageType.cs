namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The image type of a TGA header (field 3).</summary>
internal enum TgaImageType : byte
{
    /// <summary>No image data.</summary>
    None = 0,

    /// <summary>Uncompressed color-mapped image.</summary>
    ColorMapped = 1,

    /// <summary>Uncompressed true-color image.</summary>
    TrueColor = 2,

    /// <summary>Uncompressed grayscale image.</summary>
    Grayscale = 3,

    /// <summary>Run-length encoded color-mapped image.</summary>
    RleColorMapped = 9,

    /// <summary>Run-length encoded true-color image.</summary>
    RleTrueColor = 10,

    /// <summary>Run-length encoded grayscale image.</summary>
    RleGrayscale = 11,
}
