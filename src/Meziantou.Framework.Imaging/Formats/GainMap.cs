using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// A gain map to write next to an image (<see cref="JpegEncoder.GainMap"/>, <see cref="AvifEncoder.GainMap"/>): the image
/// whose samples give the gain, and the parameters that turn them into the gain (ISO 21496-1).
/// </summary>
/// <remarks>
/// <para>
/// The samples of <see cref="Image"/> are the coded values of the gain map, as <see cref="AuxiliaryImage.Decode(ImageDecodeOptions?)"/>
/// returns them: a grayscale image gives one gain for the three channels. It may be smaller than the base image, which
/// readers then cover by interpolation. Its color description (ICC profile, CICP) is the color space of the alternate
/// rendition, which a gain map that does not use the color space of the base image applies in
/// (<see cref="GainMapMetadata.UseBaseColorSpace"/>).
/// </para>
/// <para>
/// The image is borrowed, not copied: it must stay alive, and unchanged, until the encoder has written the file.
/// </para>
/// </remarks>
public sealed class GainMap
{
    /// <summary>Initializes a new instance of the <see cref="GainMap"/> class.</summary>
    /// <param name="image">The gain map image: one frame, its samples the coded values of the gain map.</param>
    /// <param name="metadata">The parameters of the gain map.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="metadata"/> is <see langword="null"/>.</exception>
    public GainMap(Image image, GainMapMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(metadata);
        Image = image;
        Metadata = metadata;
    }

    /// <summary>Gets the gain map image.</summary>
    public Image Image { get; }

    /// <summary>Gets the parameters of the gain map.</summary>
    public GainMapMetadata Metadata { get; }
}
