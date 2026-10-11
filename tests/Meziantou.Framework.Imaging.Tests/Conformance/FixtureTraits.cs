using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Properties of a fixture input that change what the cross-codec input contracts can assert. They come from formats that
/// have no end-of-image marker, and they are documented behavior, not decoder defects.
/// </summary>
internal static class FixtureTraits
{
    /// <summary>
    /// Gets a value indicating whether the last bytes of the input are only delimited by the end of the input: the bytes
    /// after the image data of a TGA file (a TGA 2.0 developer area, extension area and footer, which are located from the
    /// last 26 bytes), and the last sample of a plain (ASCII) Netpbm raster with the white space that may follow it. Every
    /// byte of the input is still read and needed. What differs is how a limit that cuts these bytes is detected on a
    /// stream: by the single discarded byte that tells the end of the input from a longer input, the only byte ever read
    /// past <see cref="ImageResourceLimits.MaxEncodedBytes"/>.
    /// </summary>
    public static bool IsDelimitedByEndOfInput(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.HasFeature("tga.trailingData") || fixture.Entry.HasFeature("pnm.raster=plain");
    }

    /// <summary>
    /// Gets a value indicating whether a proper prefix of the input can itself be a complete, valid image of the same
    /// format, so that truncation is not detectable: a TGA file whose trailing bytes are cut is a valid TGA 1.0 file, and a
    /// plain (ASCII) Netpbm raster cut inside its last sample is a complete raster with a smaller last sample.
    /// </summary>
    public static bool ProperPrefixMayBeValid(GoldenFixture fixture) => IsDelimitedByEndOfInput(fixture);
}
