using Meziantou.Framework.MediaTags;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class FormatDetectorTests
{
    [Fact]
    public void DetectFromHeader_Flac()
    {
        byte[] header = [(byte)'f', (byte)'L', (byte)'a', (byte)'C', 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(MediaFormat.Flac, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_OggVorbis()
    {
        // OGG page header (27 bytes) + 1 segment byte + vorbis ident header
        var header = new byte[36];
        header[0] = (byte)'O'; header[1] = (byte)'g'; header[2] = (byte)'g'; header[3] = (byte)'S';
        header[26] = 1; // 1 segment
        header[27] = 8; // segment size
        // Vorbis identification header
        header[28] = 0x01;
        header[29] = (byte)'v'; header[30] = (byte)'o'; header[31] = (byte)'r';
        header[32] = (byte)'b'; header[33] = (byte)'i'; header[34] = (byte)'s';
        Assert.Equal(MediaFormat.OggVorbis, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_OggOpus()
    {
        var header = new byte[36];
        header[0] = (byte)'O'; header[1] = (byte)'g'; header[2] = (byte)'g'; header[3] = (byte)'S';
        header[26] = 1; // 1 segment
        header[27] = 8; // segment size
        // Opus identification header
        header[28] = (byte)'O'; header[29] = (byte)'p'; header[30] = (byte)'u'; header[31] = (byte)'s';
        header[32] = (byte)'H'; header[33] = (byte)'e'; header[34] = (byte)'a'; header[35] = (byte)'d';
        Assert.Equal(MediaFormat.OggOpus, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_Wav()
    {
        byte[] header = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E'];
        Assert.Equal(MediaFormat.Wav, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_Aiff()
    {
        byte[] header = [(byte)'F', (byte)'O', (byte)'R', (byte)'M', 0, 0, 0, 0, (byte)'A', (byte)'I', (byte)'F', (byte)'F'];
        Assert.Equal(MediaFormat.Aiff, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_AiffC()
    {
        byte[] header = [(byte)'F', (byte)'O', (byte)'R', (byte)'M', 0, 0, 0, 0, (byte)'A', (byte)'I', (byte)'F', (byte)'C'];
        Assert.Equal(MediaFormat.Aiff, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_Mp4()
    {
        byte[] header = [0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', 0];
        Assert.Equal(MediaFormat.Mp4, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_Mp3_Id3v2()
    {
        byte[] header = [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(MediaFormat.Mp3, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_Mp3_FrameSync()
    {
        byte[] header = [0xFF, 0xFB, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(MediaFormat.Mp3, FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_Unknown()
    {
        byte[] header = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];
        Assert.Null(FormatDetector.DetectFromHeader(header));
    }

    [Fact]
    public void DetectFromHeader_TooShort()
    {
        Assert.Null(FormatDetector.DetectFromHeader([0x01, 0x02]));
    }

    [Theory]
    [InlineData(".mp3", MediaFormat.Mp3)]
    [InlineData(".ogg", MediaFormat.OggVorbis)]
    [InlineData(".oga", MediaFormat.OggVorbis)]
    [InlineData(".opus", MediaFormat.OggOpus)]
    [InlineData(".flac", MediaFormat.Flac)]
    [InlineData(".m4a", MediaFormat.Mp4)]
    [InlineData(".mp4", MediaFormat.Mp4)]
    [InlineData(".wav", MediaFormat.Wav)]
    [InlineData(".aif", MediaFormat.Aiff)]
    [InlineData(".aiff", MediaFormat.Aiff)]
    public void DetectFromExtension(string extension, MediaFormat expected)
    {
        Assert.Equal(expected, FormatDetector.DetectFromExtension("test" + extension));
    }

    [Fact]
    public void DetectFromExtension_Unknown()
    {
        Assert.Null(FormatDetector.DetectFromExtension("test.xyz"));
    }
}
