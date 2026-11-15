namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The pixel type the generic suites read an input with when the fixture is not at hand: OpenEXR, Radiance HDR, DNG and
/// 32-bit PSD samples are high dynamic range, which a reader of integers rejects, so these inputs are read as
/// <see cref="RgbaFloat"/> (exact for half and float samples); every other input is read as <see cref="Rgba64"/>.
/// </summary>
internal static class SuiteReaders
{
    /// <summary>
    /// Determines whether an input is read as <see cref="RgbaFloat"/>: OpenEXR, Radiance HDR, DNG, 32-bit PSD documents,
    /// and the DDS files of a floating-point format.
    /// </summary>
    public static bool ReadsFloats(ReadOnlySpan<byte> data) => Image.DetectFormat(data) switch
    {
        ImageFormat.OpenExr or ImageFormat.Radiance or ImageFormat.Dng => true,

        // 32-bit PSD documents
        ImageFormat.Psd => Image.Identify(data).PixelFormat == PixelFormat.RgbaFloat,
        ImageFormat.Dds => IsFloatingPointDds(data),
        _ => false,
    };

    private static bool IsFloatingPointDds(ReadOnlySpan<byte> data)
    {
        try
        {
            return Image.Identify(data).PixelFormat is PixelFormat.RgbaFloat or PixelFormat.RgbaHalf;
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
        {
            return false;
        }
    }
}
