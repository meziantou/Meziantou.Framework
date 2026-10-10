namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The compression used by <see cref="TgaEncoder"/>.</summary>
public enum TgaCompression
{
    /// <summary>Uncompressed rows (image types 2 and 3). This is the default and the most widely readable output.</summary>
    None = 0,

    /// <summary>
    /// Run-length encoded rows (image types 10 and 11). Packets never cross a scan line, as the TGA 2.0 specification
    /// recommends. The decoded pixels are identical to <see cref="None"/>: the encoding is lossless.
    /// </summary>
    RunLength = 1,
}
