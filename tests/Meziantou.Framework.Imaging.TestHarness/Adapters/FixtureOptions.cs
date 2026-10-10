using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>Turns the <c>decodeOptions</c> of a fixture (limits, frame limit) into library options.</summary>
public static class FixtureOptions
{
    /// <summary>Creates the configuration declared by the fixture (default limits when none are declared).</summary>
    public static ImageConfiguration CreateConfiguration(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var declared = fixture.Entry.DecodeOptions?.Limits;
        if (declared is null || declared.Count == 0)
            return ImageConfiguration.Default;

        var defaults = ImageResourceLimits.Default;
        long Get(string name, long fallback) => declared.TryGetValue(name, out var value) ? value : fallback;
        var limits = new ImageResourceLimits
        {
            MaxWidth = checked((int)Get("MaxWidth", defaults.MaxWidth)),
            MaxHeight = checked((int)Get("MaxHeight", defaults.MaxHeight)),
            MaxFramePixels = Get("MaxFramePixels", defaults.MaxFramePixels),
            MaxFrames = checked((int)Get("MaxFrames", defaults.MaxFrames)),
            MaxTotalPixels = Get("MaxTotalPixels", defaults.MaxTotalPixels),
            MaxEncodedBytes = Get("MaxEncodedBytes", defaults.MaxEncodedBytes),
            MaxMetadataBytes = Get("MaxMetadataBytes", defaults.MaxMetadataBytes),
            MaxLiveAllocationBytes = Get("MaxLiveAllocationBytes", defaults.MaxLiveAllocationBytes),
        };

        return new ImageConfiguration { Limits = limits };
    }

    /// <summary>Creates the decode options declared by the fixture.</summary>
    public static ImageDecodeOptions CreateDecodeOptions(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return new ImageDecodeOptions { Configuration = CreateConfiguration(fixture), FrameLimit = fixture.Entry.DecodeOptions?.FrameLimit };
    }

    /// <summary>Creates identify options with the fixture limits.</summary>
    public static ImageIdentifyOptions CreateIdentifyOptions(GoldenFixture fixture, ImageIdentifyMode mode)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return new ImageIdentifyOptions { Configuration = CreateConfiguration(fixture), Mode = mode };
    }

    /// <summary>Gets the library format of a fixture (<c>png</c>, <c>gif</c>, <c>jpeg</c>, <c>webp</c>, <c>qoi</c>, <c>bmp</c>, <c>tga</c>, <c>pnm</c>).</summary>
    public static ImageFormat GetFormat(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.Format switch
        {
            "png" => ImageFormat.Png,
            "gif" => ImageFormat.Gif,
            "jpeg" => ImageFormat.Jpeg,
            "webp" => ImageFormat.WebP,
            "qoi" => ImageFormat.Qoi,
            "bmp" => ImageFormat.Bmp,
            "tga" => ImageFormat.Tga,
            "pnm" => ImageFormat.Pnm,
            _ => ImageFormat.Unknown,
        };
    }
}
