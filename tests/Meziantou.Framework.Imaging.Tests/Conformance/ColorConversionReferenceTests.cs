using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Color;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// ICC color conversion checked against the independent high-precision reference of the test harness
/// (<see cref="ReferenceIccTransform"/>: its own profile reader, decimal arithmetic, one color at a time), over synthetic
/// profiles covering each supported model and over the profiles provided by the library.
/// </summary>
/// <remarks>
/// Tolerance: none beyond rounding. Each integer sample must be the correctly rounded exact value (ties upward); the other
/// neighbor is accepted only when the exact value lies within <see cref="ReferenceIccTransform.TieWindow"/> of a rounding
/// boundary. Floating-point samples must be within <see cref="ReferenceIccTransform.SingleTolerance"/>.
/// </remarks>
public sealed class ColorConversionReferenceTests
{
    private static readonly (string Name, Func<IccProfile> Create)[] Profiles =
    [
        ("srgb", () => IccProfile.Srgb),
        ("display-p3", () => IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve())),
        ("rgb-gamma", () => IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.Gamma(563))),
        ("rgb-sampled", () => IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, SampledPower(33, 1.8))),
        ("rgb-mixed-curves", () => IccTestProfiles.Rgb(
            IccTestProfiles.SrgbColorants,
            IccTestProfiles.Parametric(4, 2.2, 0.9, 0.1, 0.3, 0.2, 0.01, 0.005),
            IccTestProfiles.Parametric(1, 1.5, 0.75, 0.25),
            IccTestProfiles.Curve())),
        ("rgb-linear", () => IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.Curve())),
        ("srgb-gray", () => IccProfile.SrgbGray),
        ("gray-gamma", () => IccTestProfiles.Gray(IccTestProfiles.Gamma(461))),
        ("gray-sampled", () => IccTestProfiles.Gray(SampledPower(17, 2.4))),
        ("gray-lab", () => IccTestProfiles.Gray(IccTestProfiles.Parametric(2, 1.25, 0.875, 0.125, 0.03125), "Lab ")),
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var source in Profiles)
        {
            foreach (var destination in Profiles)
            {
                if (source.Name != destination.Name)
                {
                    data.Add(source.Name, destination.Name);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConversionMatchesTheReference(string sourceName, string destinationName)
    {
        var source = Profiles.Single(profile => profile.Name == sourceName).Create();
        var destination = Profiles.Single(profile => profile.Name == destinationName).Create();
        var transform = IccColorTransform.Create(source, destination);
        var reference = ReferenceIccTransform.Create(source.Data.Span, destination.Data.Span);
        Assert.Equal(reference.SourceChannelCount, transform.SourceChannelCount);
        Assert.Equal(reference.DestinationChannelCount, transform.DestinationChannelCount);

        var bytes = CreateSamples(transform.SourceChannelCount, steps: 6, randomColors: 40, byte.MaxValue).Select(value => (byte)value).ToArray();
        var convertedBytes = new byte[bytes.Length / transform.SourceChannelCount * transform.DestinationChannelCount];
        transform.Convert(bytes, convertedBytes);
        Assert.Empty(reference.Compare(bytes, convertedBytes));

        var words = CreateSamples(transform.SourceChannelCount, steps: 4, randomColors: 40, ushort.MaxValue).Select(value => (ushort)value).ToArray();
        var convertedWords = new ushort[words.Length / transform.SourceChannelCount * transform.DestinationChannelCount];
        transform.Convert(words, convertedWords);
        Assert.Empty(reference.Compare(words, convertedWords));

        var floats = CreateSamples(transform.SourceChannelCount, steps: 3, randomColors: 20, 1 << 20).Select(value => value / (float)(1 << 20)).ToArray();
        var convertedFloats = new float[floats.Length / transform.SourceChannelCount * transform.DestinationChannelCount];
        transform.Convert(floats, convertedFloats);
        Assert.Empty(reference.Compare(floats, convertedFloats));
    }

    [Fact]
    public void ReferenceReportsAWrongSampleAndAWrongLength()
    {
        var source = IccProfile.Srgb;
        var destination = Profiles.Single(profile => profile.Name == "display-p3").Create();
        var reference = ReferenceIccTransform.Create(source.Data.Span, destination.Data.Span);

        // sRGB red is (234, 51, 35) in Display P3: one unit off in any channel is reported, for every sample type
        Assert.Empty(reference.Compare([255, 0, 0], [234, 51, 35]));
        Assert.Single(reference.Compare([255, 0, 0], [(byte)235, 51, 35]));
        Assert.Single(reference.Compare([255, 0, 0], [(byte)234, 50, 35]));
        Assert.Single(reference.Compare([255, 0, 0], [(byte)234, 51, 36]));
        Assert.Empty(reference.Compare([(ushort)65535, 0, 0], [(ushort)60127, 13124, 9094]));
        Assert.Single(reference.Compare([(ushort)65535, 0, 0], [(ushort)60127, 13125, 9094]));

        var exact = reference.Convert([1m, 0m, 0m]);
        Assert.True(Math.Abs(exact[0] - 0.9175m) < 0.0001m && Math.Abs(exact[1] - 0.2003m) < 0.0001m && Math.Abs(exact[2] - 0.1388m) < 0.0001m);
        Assert.Empty(reference.Compare([1f, 0f, 0f], [(float)exact[0], (float)exact[1], (float)exact[2]]));
        Assert.Single(reference.Compare([1f, 0f, 0f], [(float)exact[0], (float)exact[1] + 0.000001f, (float)exact[2]]));
        Assert.Single(reference.Compare([1f, 0f, 0f], [float.NaN, (float)exact[1], (float)exact[2]]));

        Assert.Single(reference.Compare([255, 0, 0], [(byte)234, 51]));
        Assert.Single(reference.Compare([255, 0], [(byte)234, 51, 35]));
    }

    /// <summary>A sampled curve of x^gamma, rounded to 16 bits.</summary>
    private static byte[] SampledPower(int count, double gamma)
    {
        var entries = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = (ushort)Math.Round(Math.Pow((double)i / (count - 1), gamma) * 65535, MidpointRounding.AwayFromZero);
        }

        return IccTestProfiles.Curve(entries);
    }

    /// <summary>
    /// Interleaved colors: a regular grid including both ends of each channel (and the neighbors of the ends), then
    /// reproducible pseudo-random colors.
    /// </summary>
    private static int[] CreateSamples(int channels, int steps, int randomColors, int maximum)
    {
        var levels = new List<int> { 0, 1, maximum - 1, maximum };
        for (var i = 1; i < steps; i++)
        {
            levels.Add((int)((long)maximum * i / steps));
        }

        var samples = new List<int>();
        var indexes = new int[channels];
        while (true)
        {
            foreach (var index in indexes)
            {
                samples.Add(levels[index]);
            }

            var channel = 0;
            while (channel < channels && ++indexes[channel] == levels.Count)
            {
                indexes[channel++] = 0;
            }

            if (channel == channels)
                break;
        }

        // A linear congruential generator (Numerical Recipes constants): reproducible on every platform
        var state = 0x1234_5678u;
        for (var i = 0; i < randomColors * channels; i++)
        {
            state = (state * 1664525) + 1013904223;
            samples.Add((int)((ulong)(state >> 8) * ((ulong)maximum + 1) >> 24));
        }

        return [.. samples];
    }
}
