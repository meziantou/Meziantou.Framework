using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The image a JPEG 2000 file describes, resolved by <see cref="Jpeg2000FeaturePolicy"/> from the components of the
/// codestream and from the header boxes of the file: the colors, where the samples of each channel come from, and the
/// working representation of the image.
/// </summary>
internal sealed class Jpeg2000ChannelLayout
{
    public Jpeg2000ChannelLayout(Jpeg2000ColorKind kind, Jpeg2000Channel[] colors, Jpeg2000Channel? alpha, Jpeg2000Palette? palette, byte[]? profile)
    {
        Kind = kind;
        Colors = colors;
        Alpha = alpha;
        Palette = palette;
        Profile = profile;

        var precision = 0;
        foreach (var channel in colors)
        {
            precision = Math.Max(precision, channel.Precision);
        }

        BitsPerComponent = precision;
        IsSixteenBit = precision > 8 || alpha is { Precision: > 8 };
        DefaultPixelFormat = DefaultPixelFormats.ForJpeg2000(kind == Jpeg2000ColorKind.Gray, alpha is not null, IsSixteenBit);
    }

    /// <summary>Gets the interpretation of the color channels.</summary>
    public Jpeg2000ColorKind Kind { get; }

    /// <summary>Gets the color channels, in the order of the colors: one gray level; red, green, blue; luminance and chrominances; or four inks.</summary>
    public Jpeg2000Channel[] Colors { get; }

    /// <summary>Gets the channel that holds the straight opacity of the image, or <see langword="null"/>.</summary>
    public Jpeg2000Channel? Alpha { get; }

    /// <summary>Gets the palette, when a channel comes from it.</summary>
    public Jpeg2000Palette? Palette { get; }

    /// <summary>Gets the ICC profile that describes the colors, or <see langword="null"/>. Its bytes are charged to the metadata limit.</summary>
    public byte[]? Profile { get; }

    /// <summary>Gets the largest number of bits of the samples of a color channel.</summary>
    public int BitsPerComponent { get; }

    /// <summary>Gets a value indicating whether a channel has more than 8 bits: the image is then held with 16-bit samples.</summary>
    public bool IsSixteenBit { get; }

    /// <summary>Gets the default pixel format of the image.</summary>
    public PixelFormat DefaultPixelFormat { get; }

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel
    {
        get
        {
            if (Palette is not null)
                return ImageColorModel.Indexed;

            return Kind switch
            {
                Jpeg2000ColorKind.Gray => Alpha is null ? ImageColorModel.Grayscale : ImageColorModel.GrayscaleAlpha,
                Jpeg2000ColorKind.Rgb => Alpha is null ? ImageColorModel.Rgb : ImageColorModel.Rgba,
                Jpeg2000ColorKind.Sycc => ImageColorModel.YCbCr,
                _ => ImageColorModel.Cmyk,
            };
        }
    }

    /// <summary>Gets the color space an ICC profile must declare to describe the color channels.</summary>
    public IccProfileColorSpace ProfileColorSpace => Kind switch
    {
        Jpeg2000ColorKind.Gray => IccProfileColorSpace.Gray,
        Jpeg2000ColorKind.Cmyk => IccProfileColorSpace.Cmyk,
        _ => IccProfileColorSpace.Rgb,
    };
}
