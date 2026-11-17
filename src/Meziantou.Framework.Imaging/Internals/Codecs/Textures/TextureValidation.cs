namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The checks of the texture encoders that depend on the pixels: what the format of the texels cannot store is rejected
/// before anything is written, unless a setting allows the loss.
/// </summary>
internal static class TextureValidation
{
    private const float LargestHalf = 65504f;

    /// <summary>Determines whether encoding pixels of a format to a target reduces their precision.</summary>
    public static bool ReducesPrecision(PixelFormat source, TextureTarget target)
    {
        var bits = PixelFormats.GetBitsPerComponent(source);
        if (source == PixelFormat.RgbaFloat)
            return target.StoredBits < 32;

        if (source == PixelFormat.RgbaHalf)
            return !target.IsFloatingPoint || target.IsSignedNormalized;

        // Integer samples: a half keeps 11 significant bits
        var stored = target.IsHalf ? 11 : target.StoredBits;
        return bits > stored;
    }

    /// <summary>Throws when a pixel format cannot be encoded to a target without a reduction of precision that is not allowed.</summary>
    public static void EnsurePrecision(PixelFormat source, TextureTarget target, bool allowBitDepthReduction, ImageFormat format, string formatName, string encoderName)
    {
        if (!allowBitDepthReduction && ReducesPrecision(source, target))
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"{formatName} stores samples of {target.StoredBits} bits{(target.IsHalf ? " (half-precision floats)" : "")}, so encoding {source} pixels would discard precision. Set {encoderName}.AllowBitDepthReduction to reduce the samples, choose another texture format, or convert the image explicitly with CloneAs."), format, "Bit depth reduction");
    }

    /// <summary>Checks every texel of a surface: the channels the format lacks, and the values it cannot represent.</summary>
    /// <exception cref="UnsupportedImageFeatureException">A texel cannot be stored.</exception>
    public static void Validate(TexturePixels pixels, TextureTarget target, bool allowChannelReduction, ImageFormat format, string formatName, string encoderName, CancellationToken cancellationToken)
    {
        Span<float> texel = stackalloc float[4];
        var checksFloats = pixels.Precision == TexturePrecision.Float && target.Encoding != TextureEncoding.RgbaFloat;
        for (var y = 0; y < pixels.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < pixels.Width; x++)
            {
                pixels.GetPixel(x, y, texel);
                if (!allowChannelReduction && GetLostChannel(texel, target.Channels) is { } lost)
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The texel ({x}, {y}) has {lost}, which {formatName} does not store. Set {encoderName}.AllowChannelReduction to drop the channels the format lacks, or choose another texture format."), format, "Channel reduction");

                if (checksFloats)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        if (GetInvalidValue(texel[c], target) is { } reason)
                            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The sample {texel[c]} of the texel ({x}, {y}) cannot be stored: {formatName} {reason}. Clamp the samples of the image first."), format, "Samples outside the range of the format");
                    }
                }
            }
        }
    }

    private static string? GetLostChannel(ReadOnlySpan<float> texel, TextureChannels channels)
    {
        if (channels != TextureChannels.Rgba && texel[3] != 1f)
            return "an alpha other than opaque";

        return channels switch
        {
            TextureChannels.Gray when texel[1] != texel[0] || texel[2] != texel[0] => "colors that are not gray",
            TextureChannels.RedGreen when texel[2] != 0 => "a blue other than 0",
            _ => null,
        };
    }

    private static string? GetInvalidValue(float value, TextureTarget target)
    {
        if (target.IsSignedNormalized)
            return value is >= -1f and <= 1f ? null : "stores signed values from -1 to 1";

        if (target.Encoding == TextureEncoding.RgbaHalf)
            return float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) <= LargestHalf ? null : "stores half-precision floats, whose largest finite value is 65,504";

        if (target.Encoding == TextureEncoding.Bc6H)
            return value is >= 0f and <= LargestHalf ? null : "stores unsigned half-precision floats, finite, from 0 to 65,504";

        if (target.Encoding == TextureEncoding.Bc6HSigned)
            return value is >= -LargestHalf and <= LargestHalf ? null : "stores finite half-precision floats, from -65,504 to 65,504";

        return null;
    }
}
