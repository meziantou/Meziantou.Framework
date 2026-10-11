namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>The Huffman tables written by <see cref="JpegTestImage.Encode"/>.</summary>
internal enum JpegTestHuffmanStyle
{
    /// <summary>Every DC symbol has a 4-bit code, every AC symbol an 8-bit code (all resolved by the lookup table).</summary>
    Fixed,

    /// <summary>Seeded code lengths from 2 to 16 bits assigned to a random permutation of the symbols (many long codes).</summary>
    Skewed,
}
