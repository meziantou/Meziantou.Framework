using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// BMP encoder output decoded by FFmpeg's independent codec (libavcodec <c>bmp</c>): the output of the encoder, in every
/// option combination, decodes exactly in FFmpeg. The decoder is covered by the golden corpus (FFmpeg-written inputs
/// included); the committed references are re-verified against the installed FFmpeg by the corpus generator (<c>verify</c>).
/// </summary>
public sealed class BmpInteropTests
{
    public static TheoryData<string, string> EncoderCases() => LosslessEncoderInterop.EncoderCases();

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public Task EncoderOutputDecodesExactlyInFFmpeg(string id, string format) =>
        LosslessEncoderInterop.AssertEncoderOutputDecodesExactlyInFFmpegAsync(id, format, nameof(BmpInteropTests),
            [
                (new BmpEncoder { MetadataHandling = MetadataHandling.Strip }, "bmp", "-" + BmpPixelLayout.Auto),
                (new BmpEncoder { PixelLayout = BmpPixelLayout.Bgra32, MetadataHandling = MetadataHandling.Strip }, "bmp", "-" + BmpPixelLayout.Bgra32),
            ],
            // FFmpeg's BMP decoder forces an all-zero alpha channel to opaque (a workaround for writers that leave the
            // fourth byte at zero); our output declares an explicit alpha mask, so the color samples are what must agree
            source => IsFullyTransparent(source) ? WithOpaqueAlpha(source) : source);

    private static bool IsFullyTransparent(RawPixelBuffer source)
    {
        if (source.Layout.ChannelCount != 4)
            return false;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetSample(x, y, 3) != 0)
                    return false;
            }
        }

        return true;
    }

    private static RawPixelBuffer WithOpaqueAlpha(RawPixelBuffer source)
    {
        var builder = new RawPixelBufferBuilder(source.Width, source.Height, source.Layout);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    builder.SetSample(x, y, c, source.GetSample(x, y, c));
                }

                builder.SetSample(x, y, 3, source.Layout.MaxSampleValue);
            }
        }

        return builder.Build();
    }
}
