namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The pixel type the generic suites read an input with when the fixture is not at hand: OpenEXR, Radiance HDR, DNG,
/// 32-bit PSD, floating-point DDS and floating-point DPX samples are high dynamic range, which a reader of integers
/// rejects, so these inputs are read as
/// <see cref="RgbaFloat"/> (exact for half and float samples); every other input is read as <see cref="Rgba64"/>.
/// </summary>
internal static class SuiteReaders
{
    /// <summary>
    /// Determines whether an input is read as <see cref="RgbaFloat"/>: OpenEXR, Radiance HDR, DNG, 32-bit PSD documents,
    /// the DDS files of a floating-point format and floating-point DPX elements.
    /// </summary>
    public static bool ReadsFloats(ReadOnlySpan<byte> data) => Image.DetectFormat(data) switch
    {
        ImageFormat.OpenExr or ImageFormat.Radiance or ImageFormat.Dng => true,

        // 32-bit PSD documents
        ImageFormat.Psd => PixelFormats.IsFloatingPoint(Image.Identify(data).PixelFormat),
        ImageFormat.Dds => IsFloatingPointDds(data),

        // A DPX element of 32 or 64-bit components (the bit depth of the first image element, field 21.9)
        ImageFormat.Dpx => data.Length > 803 && data[803] >= 32,
        _ => false,
    };

    private static bool IsFloatingPointDds(ReadOnlySpan<byte> data)
    {
        try
        {
            return PixelFormats.IsFloatingPoint(Image.Identify(data).PixelFormat);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
        {
            return false;
        }
    }
}
