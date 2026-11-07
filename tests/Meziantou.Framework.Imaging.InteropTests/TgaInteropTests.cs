using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// TGA encoder output decoded by FFmpeg's independent codec (libavcodec <c>targa</c>): the output of the encoder, with and
/// without run-length packets, decodes exactly in FFmpeg. The decoder is covered by the golden corpus (FFmpeg-written inputs
/// included); the committed references are re-verified against the installed FFmpeg by the corpus generator (<c>verify</c>).
/// </summary>
public sealed class TgaInteropTests
{
    public static TheoryData<string, string> EncoderCases() => LosslessEncoderInterop.EncoderCases();

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public Task EncoderOutputDecodesExactlyInFFmpeg(string id, string format) =>
        LosslessEncoderInterop.AssertEncoderOutputDecodesExactlyInFFmpegAsync(id, format, nameof(TgaInteropTests),
            [
                (new TgaEncoder { MetadataHandling = MetadataHandling.Strip }, "tga", "-" + TgaCompression.None),
                (new TgaEncoder { Compression = TgaCompression.RunLength, MetadataHandling = MetadataHandling.Strip }, "tga", "-" + TgaCompression.RunLength),
            ]);
}
