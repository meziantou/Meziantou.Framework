namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// How hard the block compressors of <see cref="DdsEncoder"/> and <see cref="Ktx2Encoder"/> search for the best encoding of
/// each block (BC1 to BC7, ETC2, EAC, ASTC). It has no effect on uncompressed formats.
/// </summary>
/// <remarks>
/// Both levels are deterministic: the same pixels and settings give the same bytes on every platform. The error each format
/// reaches at each level is documented on the pages of the DDS and KTX 2.0 formats.
/// </remarks>
public enum TextureCompressionQuality
{
    /// <summary>
    /// One good attempt per block: the modes that suit most blocks, with endpoints fitted to the principal axis of the colors
    /// and refined once. Suitable for large textures and for previews.
    /// </summary>
    Fast = 0,

    /// <summary>
    /// A wider search per block: more modes, the partitions of the formats that have them, and more endpoint refinement. This
    /// is the default. It is several times slower than <see cref="Fast"/>.
    /// </summary>
    Thorough = 1,
}
