namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The parser of the LZMA encoder.</summary>
internal enum LzmaEncoderMode
{
    /// <summary>A greedy parser with a lookahead of one byte (GetOptimumFast of the LZMA SDK).</summary>
    Fast,

    /// <summary>The optimal parser of the LZMA SDK, which compares the prices of the paths of up to 4096 bytes.</summary>
    Normal,
}
