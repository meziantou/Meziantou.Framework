using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// Independent encoder inputs taken from the golden corpus: every displayed frame and separate poster of every
/// valid fixture, as raw reference buffers in the layout of each pixel format that can hold them exactly (the 16-bit or
/// 8-bit RGBA reference, BGRA for 8-bit sources, RGB when every frame is opaque, and the gray references when the corpus has
/// them). Encoder tests import these pixels, encode them, decode the output with an independent decoder and compare the
/// result with the same reference buffer. Decoder inputs written by FFmpeg from seeded noise (the generator's
/// <c>*FfmpegDecoding</c> builders) are excluded: they exist to exercise decoder paths, and noise is not representative
/// content for the encoders' quality criteria.
/// </summary>
public static class EncoderSources
{
    /// <summary>Gets every (fixture, pixel format) pair.</summary>
    /// <param name="corpus">The corpus.</param>
    /// <returns>The pairs.</returns>
    public static IEnumerable<(GoldenFixture Fixture, PixelFormat Format)> GetCases(GoldenCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        foreach (var fixture in corpus.Fixtures.Where(fixture => fixture.IsValid && !IsFfmpegDecoderInput(fixture)))
        {
            var sixteenBit = fixture.CanonicalLayout == RawPixelLayout.Rgba16Le;
            yield return (fixture, sixteenBit ? PixelFormat.Rgba64 : PixelFormat.Rgba32);
            if (!sixteenBit)
            {
                yield return (fixture, PixelFormat.Bgra32);
                if (GetStills(fixture, PixelFormat.Rgba32).All(still => RawImport.IsOpaque(still.Pixels)))
                {
                    yield return (fixture, PixelFormat.Rgb24);
                }
            }

            if (fixture.Layouts.Contains(RawPixelLayout.Gray16Le))
            {
                yield return (fixture, PixelFormat.Gray16);
            }

            if (fixture.Layouts.Contains(RawPixelLayout.Gray8))
            {
                yield return (fixture, PixelFormat.Gray8);
            }
        }
    }

    /// <summary>Gets the frames and the separate poster of a fixture as still images in the layout of <paramref name="format"/>.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="format">The pixel format.</param>
    /// <returns>The named references (<c>frame N</c>, <c>poster</c>).</returns>
    public static IReadOnlyList<(string Name, RawPixelBuffer Pixels)> GetStills(GoldenFixture fixture, PixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var result = new List<(string, RawPixelBuffer)>();
        for (var i = 0; i < fixture.Expected.FrameCount; i++)
        {
            result.Add((string.Create(CultureInfo.InvariantCulture, $"frame {i}"), Convert(fixture.GetFrame(i, GetSourceLayout(format)), format)));
        }

        if (fixture.Expected.Poster is not null)
        {
            result.Add(("poster", Convert(fixture.GetPoster(GetSourceLayout(format)), format)));
        }

        return result;
    }

    /// <summary>Imports a reference buffer as a single-frame image of a pixel format (public raw import API).</summary>
    /// <param name="pixels">The reference, in the layout <see cref="ImageSnapshots.GetLayout"/> of <paramref name="format"/>.</param>
    /// <param name="format">The pixel format.</param>
    /// <returns>The image.</returns>
    public static Image CreateImage(RawPixelBuffer pixels, PixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var bytes = RawImport.ToPixelBytes(pixels, format);
        return format switch
        {
            PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(bytes, pixels.Width, pixels.Height),
            PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(bytes, pixels.Width, pixels.Height),
            PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(bytes, pixels.Width, pixels.Height),
            PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(bytes, pixels.Width, pixels.Height),
            PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(bytes, pixels.Width, pixels.Height),
            PixelFormat.Gray16 => Image.ImportPixelBytes<Gray16>(bytes, pixels.Width, pixels.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format."),
        };
    }

    private static RawPixelLayout GetSourceLayout(PixelFormat format) => format == PixelFormat.Rgb24 ? RawPixelLayout.Rgba8 : ImageSnapshots.GetLayout(format);

    private static RawPixelBuffer Convert(RawPixelBuffer source, PixelFormat format) => format == PixelFormat.Rgb24 ? RawImport.DropOpaqueAlpha(source) : source;

    /// <summary>Gets a value indicating whether the fixture is a decoder input written by FFmpeg from seeded noise.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns><see langword="true"/> for the fixtures of the generator's <c>*FfmpegDecoding</c> builders.</returns>
    public static bool IsFfmpegDecoderInput(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.Provenance.Generator?.Contains("FfmpegDecoding ", StringComparison.Ordinal) == true;
    }
}
