namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The pixel type the generic suites read an input with when the fixture is not at hand: OpenEXR, Radiance HDR and
/// floating-point DPX samples are high dynamic range, which a reader of integers rejects, so their inputs are read as
/// <see cref="RgbaFloat"/> (exact for half and float samples); every other input is read as <see cref="Rgba64"/>.
/// </summary>
internal static class SuiteReaders
{
    /// <summary>Determines whether an input is read as <see cref="RgbaFloat"/>.</summary>
    public static bool ReadsFloats(ReadOnlySpan<byte> data) => Image.DetectFormat(data) switch
    {
        ImageFormat.OpenExr or ImageFormat.Radiance => true,

        // A DPX element of 32 or 64-bit components (the bit depth of the first image element, field 21.9)
        ImageFormat.Dpx => data.Length > 803 && data[803] >= 32,
        _ => false,
    };
}
