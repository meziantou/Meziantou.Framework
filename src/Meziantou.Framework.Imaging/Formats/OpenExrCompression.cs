namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The lossless compression of the chunks of an OpenEXR file written by <see cref="OpenExrEncoder"/>.</summary>
public enum OpenExrCompression
{
    /// <summary>No compression (<c>NO_COMPRESSION</c>): one scan line per chunk; the output does not need a seekable destination.</summary>
    None = 0,

    /// <summary>Run-length coding of byte differences (<c>RLE_COMPRESSION</c>), one scan line per chunk.</summary>
    Rle = 1,

    /// <summary>zlib compression of byte differences (<c>ZIPS_COMPRESSION</c>), one scan line per chunk.</summary>
    Zips = 2,

    /// <summary>zlib compression of byte differences (<c>ZIP_COMPRESSION</c>), 16 scan lines per chunk. This is the default.</summary>
    Zip = 3,

    /// <summary>Wavelet and Huffman coding (<c>PIZ_COMPRESSION</c>), 32 scan lines per chunk: usually the smallest for photographs and noisy images.</summary>
    Piz = 4,
}
