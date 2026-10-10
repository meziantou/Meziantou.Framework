using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Properties of a fixture input that change what the cross-codec input contracts can assert. Both come from formats that
/// have no end-of-image marker, and both are documented behavior, not decoder defects.
/// </summary>
internal static class FixtureTraits
{
    /// <summary>
    /// Gets a value indicating whether the input has bytes after the image data that the decoder never reads (a TGA 2.0
    /// developer area, extension area and footer: they are located by offsets stored at the end of the file, which a
    /// forward-only decoder cannot follow). The decoder then consumes fewer bytes than the
    /// file holds, so the encoded-byte requirement is smaller than the file length and no I/O failure in the trailing
    /// bytes can be observed.
    /// </summary>
    public static bool HasUnreadTrailingBytes(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.HasFeature("tga.trailingData");
    }

    /// <summary>
    /// Gets a value indicating whether a proper prefix of the input can itself be a complete, valid image of the same
    /// format, so that truncation is not detectable: a TGA file whose trailing bytes are cut is a valid TGA 1.0 file, and a
    /// plain (ASCII) Netpbm raster cut inside its last sample is a complete raster with a smaller last sample.
    /// </summary>
    public static bool ProperPrefixMayBeValid(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return HasUnreadTrailingBytes(fixture) || fixture.Entry.HasFeature("pnm.raster=plain");
    }
}
