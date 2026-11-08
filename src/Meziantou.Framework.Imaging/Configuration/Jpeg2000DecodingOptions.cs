namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the JPEG 2000 decoder (<see cref="ImageConfiguration.Jpeg2000Decoding"/>): the quality layers it
/// decodes.
/// </summary>
/// <remarks>
/// They apply to every operation that decodes the pixels of a JPEG 2000 file: eager loads, sequential readers and
/// collections. Identification reads no pixel and is not affected.
/// </remarks>
public sealed class Jpeg2000DecodingOptions
{
    /// <summary>Gets the default options: every quality layer decoded.</summary>
    public static Jpeg2000DecodingOptions Default { get; } = new();

    /// <summary>
    /// Gets the number of quality layers decoded, or <see langword="null"/> (the default) to decode all of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A JPEG 2000 codestream holds its coding passes in quality layers: the first layer gives a coarse image, and each
    /// layer that follows refines it, up to the full quality of the file. With a limit, the coding passes of the next
    /// layers are not decoded: the image is the one a file cut after the last of these layers would give, as
    /// <c>opj_decompress -l</c> gives it. A limit at least the number of layers of a tile decodes the whole tile.
    /// </para>
    /// <para>
    /// The packets of the other layers are still read, so that the decoder finds the packets it needs: the time saved is the
    /// one of the coding passes. When the packets come layer by layer (the LRCP progression without progression change),
    /// the decoder stops at the last layer it needs.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? QualityLayerLimit
    {
        get;
        init
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value);
            }

            field = value;
        }
    }
}
