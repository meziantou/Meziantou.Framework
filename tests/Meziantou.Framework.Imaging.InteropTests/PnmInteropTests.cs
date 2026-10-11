using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Netpbm encoder output decoded by FFmpeg's independent codecs (libavcodec <c>pgm</c>/<c>ppm</c>/<c>pam</c>): the binary
/// output of the encoder decodes exactly in FFmpeg. The decoder is covered by the golden corpus (FFmpeg-written inputs
/// included); the committed references are re-verified against the installed FFmpeg by the corpus generator (<c>verify</c>).
/// </summary>
public sealed class PnmInteropTests
{
    public static TheoryData<string, string> EncoderCases() => LosslessEncoderInterop.EncoderCases();

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public Task EncoderOutputDecodesExactlyInFFmpeg(string id, string format)
    {
        var extension = Enum.Parse<PixelFormat>(format) switch
        {
            PixelFormat.Gray8 => "pgm",
            PixelFormat.Rgb24 => "ppm",
            _ => "pam",
        };

        return LosslessEncoderInterop.AssertEncoderOutputDecodesExactlyInFFmpegAsync(id, format, nameof(PnmInteropTests),
            [(new PnmEncoder { MetadataHandling = MetadataHandling.Strip }, extension, "")]);
    }
}
