using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// ICC profiles must only throw the exceptions the library documents for invalid input. The named colors of a named color
/// profile (versions 2, 4 and 5) are found by their name and convert to sRGB in [0, 1]; the conversions of a profile to
/// sRGB and from it, through the processing elements and the calculator of iccMAX profiles, give samples in [0, 1] that do
/// not depend on the colors converted before.
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The signatures of the profile classes, color spaces, tags, tag types, processing elements and calculator operations.
    // A property: All, in another file of the class, reads it, and the initializers of the files of a partial class run in
    // no defined order
    private static string[] IccTokens =>
    [
        "acsp", "mntr", "prtr", "scnr", "spac", "abst", "link", "nmcl", "RGB ", "GRAY", "CMYK", "XYZ ", "Lab ", "\0\0\0\0",
        "A2B0", "A2B1", "A2B2", "A2B3", "B2A0", "B2A1", "B2A2", "B2A3", "wtpt", "c2sp", "ncl2", "mpet", "cvst", "matf", "clut",
        "calc", "tint", "bACS", "eACS", "curf", "parf", "samf", "sngf", "clcf", "func", "clmt", "data", "in  ", "out ", "tget",
        "tput", "tsav", "env ", "curv", "mtx ", "elem", "copy", "rotl", "rotr", "posd", "flip", "pop ", "solv", "tran", "sum ",
        "prod", "min ", "max ", "and ", "or  ", "if  ", "else", "sel ", "case", "dflt", "tLab", "tXYZ", "add ", "sub ", "mul ",
        "div ", "pow ", "gama", "sqrt", "eq  ", "lt  ", "not ", "rnum", "ctop", "ptoc", "NaN ", "+INF", "true", "tary", "tstr",
        "ncol", "tnt0", "name", "pcs ", "dev ", "utf8", "fl32", "fl16", "ui16", "ui08", "\u0005\0\0\0", "\u0004@\0\0",
        "\u0002@\0\0", "\u007Fÿÿÿ", "\u0080\0\0\0",
    ];

    // Colors of every device channel count: the corners of the device space and points between them
    private static readonly float[] IccSampleLevels = [0, 0.25f, 0.5f, 0.75f, 1];

    private static void IccNamedColorsTarget(ReadOnlySpan<byte> data)
    {
        var profile = new IccProfile(new MetadataBlob(data.ToArray()));
        if (profile.ProfileClass != IccProfileClass.NamedColor)
            return;

        IccNamedColorCollection colors;
        try
        {
            colors = IccNamedColorCollection.Create(profile);
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
        {
            return;
        }

        for (var i = 0; i < colors.Count; i++)
        {
            var color = colors[i];
            FuzzAssert.Equal(colors.DeviceChannelCount, color.DeviceValues.Count, "The device values of a color");
            var index = colors.IndexOf(color.Name);
            FuzzAssert.True(index >= 0 && index <= i && colors[index].Name == color.Name, "A color is found by its name");
        }

        // A collection that reads converts: only the destination of the sequence can be refused
        IccNamedColorTransform transform;
        try
        {
            transform = IccNamedColorTransform.Create(profile, IccProfile.Srgb);
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
        {
            FuzzAssert.True(false, "The named colors are read but do not convert: " + exception.Message);
            return;
        }

        Span<float> rgb = stackalloc float[3];
        for (var i = 0; i < Math.Min(colors.Count, 64); i++)
        {
            transform.Convert(colors[i], rgb);
            foreach (var value in rgb)
            {
                FuzzAssert.True(value is >= 0 and <= 1, "A converted named color is in [0, 1]");
            }
        }
    }

    private static void IccProfileConversionTarget(ReadOnlySpan<byte> data)
    {
        var profile = new IccProfile(new MetadataBlob(data.ToArray()));

        // The rendering intent declared in the header selects the tags
        var intent = (IccRenderingIntent)((profile.RenderingIntent is { } declared ? (int)declared : 0) & 3);
        var options = new IccColorTransformOptions { Intent = intent, BlackPointCompensation = false };
        CheckConversion(() => IccColorTransform.Create(profile, IccProfile.Srgb, options));
        CheckConversion(() => IccColorTransform.Create(IccProfile.Srgb, profile, options));
    }

    private static void CheckConversion(Func<IccColorTransform> create)
    {
        IccColorTransform transform;
        try
        {
            transform = create();
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
        {
            return;
        }

        // Every combination of the levels for up to three channels, the levels on the diagonal for four
        var channels = transform.SourceChannelCount;
        var colors = channels == 4 ? IccSampleLevels.Length : (int)Math.Pow(IccSampleLevels.Length, channels);
        var source = new float[colors * channels];
        for (var color = 0; color < colors; color++)
        {
            var remainder = color;
            for (var channel = 0; channel < channels; channel++)
            {
                source[(color * channels) + channel] = IccSampleLevels[channels == 4 ? color : remainder % IccSampleLevels.Length];
                remainder /= IccSampleLevels.Length;
            }
        }

        var floats = new float[colors * transform.DestinationChannelCount];
        transform.Convert(source, floats);
        foreach (var value in floats)
        {
            FuzzAssert.True(value is >= 0 and <= 1, "A converted sample is in [0, 1]");
        }

        // The colors in the reverse order give the same samples: nothing of a color (the temporary channels of a
        // calculator, the scratch memory of the processing elements) leaks into the next one
        var reversed = new float[source.Length];
        for (var color = 0; color < colors; color++)
        {
            source.AsSpan(color * channels, channels).CopyTo(reversed.AsSpan((colors - 1 - color) * channels));
        }

        var again = new float[floats.Length];
        transform.Convert(reversed, again);
        var outputs = transform.DestinationChannelCount;
        for (var color = 0; color < colors; color++)
        {
            FuzzAssert.True(again.AsSpan((colors - 1 - color) * outputs, outputs).SequenceEqual(floats.AsSpan(color * outputs, outputs)), "A color converts to the same samples whatever the colors before it");
        }

        var samples16 = new ushort[floats.Length];
        transform.Convert(Array.ConvertAll(source, value => (ushort)Math.Round(value * 65535, MidpointRounding.AwayFromZero)), samples16);
        var samples8 = new byte[floats.Length];
        transform.Convert(Array.ConvertAll(source, value => (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero)), samples8);
    }

    private static IEnumerable<FuzzSeed> GetIccSeeds()
    {
        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Seeds/icc/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new FuzzSeed(name["Seeds/icc/".Length..^".icc".Length], buffer.ToArray());
        }
    }
}
